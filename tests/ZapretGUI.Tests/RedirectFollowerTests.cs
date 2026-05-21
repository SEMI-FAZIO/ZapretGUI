using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ZapretGUI.Services;

namespace ZapretGUI.Tests;

public class RedirectFollowerTests
{
    [Fact]
    public async Task FollowRedirects_RejectsRedirectToDisallowedHost()
    {
        // Real-world threat model: GitHub Releases API returns a browser_download_url
        // on an allowed host (github.com). The handler at github.com returns 302 to
        // an attacker host. With AllowAutoRedirect=true, HttpClient would silently
        // fetch the attacker payload — this test proves we don't.
        var handler = new ScriptedHandler()
            .EnqueueRedirect("https://github.com/Flowseal/zapret-discord-youtube/releases/download/v1/zapret.zip",
                             "https://evil.attacker.com/payload.zip");
        using var http = new HttpClient(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ZapretInstaller.GetFollowingAllowedRedirectsAsync(http,
                "https://github.com/Flowseal/zapret-discord-youtube/releases/download/v1/zapret.zip",
                CancellationToken.None));

        Assert.Contains("evil.attacker.com", ex.Message);
        Assert.Equal(1, handler.RequestCount);  // only the initial github.com request — never followed to evil.com
    }

    [Fact]
    public async Task FollowRedirects_FollowsRealisticGitHubRedirectChain()
    {
        // This mirrors actual GitHub behavior: github.com → objects.githubusercontent.com (signed S3-style URL).
        var handler = new ScriptedHandler()
            .EnqueueRedirect("https://github.com/Flowseal/zapret-discord-youtube/releases/download/v1/zapret.zip",
                             "https://objects.githubusercontent.com/release-asset/abc?token=signed")
            .EnqueueOk("https://objects.githubusercontent.com/release-asset/abc?token=signed", "ZIP_BYTES");
        using var http = new HttpClient(handler);

        using var resp = await ZapretInstaller.GetFollowingAllowedRedirectsAsync(http,
            "https://github.com/Flowseal/zapret-discord-youtube/releases/download/v1/zapret.zip",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("ZIP_BYTES", await resp.Content.ReadAsStringAsync());
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task FollowRedirects_CapsAtMaxRedirects()
    {
        var handler = new ScriptedHandler();
        for (int i = 0; i < 10; i++)
            handler.EnqueueRedirect($"https://github.com/hop-{i}", $"https://github.com/hop-{i + 1}");

        using var http = new HttpClient(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ZapretInstaller.GetFollowingAllowedRedirectsAsync(http,
                "https://github.com/hop-0", CancellationToken.None, maxRedirects: 3));

        Assert.Contains("Слишком много", ex.Message);
        // initial + 3 redirects = 4 requests
        Assert.Equal(4, handler.RequestCount);
    }

    [Fact]
    public async Task FollowRedirects_RejectsInitialUrlOffAllowlist()
    {
        var handler = new ScriptedHandler();
        using var http = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ZapretInstaller.GetFollowingAllowedRedirectsAsync(http,
                "https://evil.com/asset.zip", CancellationToken.None));

        Assert.Equal(0, handler.RequestCount);  // never even contacted
    }

    [Fact]
    public async Task FollowRedirects_RejectsHttpScheme()
    {
        var handler = new ScriptedHandler();
        using var http = new HttpClient(handler);

        // Even a plaintext URL whose host is on the allowlist must be rejected:
        // a MITM could otherwise rewrite the body.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ZapretInstaller.GetFollowingAllowedRedirectsAsync(http,
                "http://github.com/asset.zip", CancellationToken.None));

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task FollowRedirects_RejectsRedirectToHttpDowngrade()
    {
        // Allowed host but HTTPS→HTTP downgrade in the Location header — must reject.
        var handler = new ScriptedHandler()
            .EnqueueRedirect("https://github.com/asset.zip", "http://github.com/asset.zip");
        using var http = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ZapretInstaller.GetFollowingAllowedRedirectsAsync(http,
                "https://github.com/asset.zip", CancellationToken.None));
    }

    [Fact]
    public async Task FollowRedirects_HandlesAllRedirectStatusCodes()
    {
        foreach (var code in new[]
        {
            HttpStatusCode.MovedPermanently,    // 301
            HttpStatusCode.Found,               // 302
            HttpStatusCode.SeeOther,            // 303
            HttpStatusCode.TemporaryRedirect,   // 307
            HttpStatusCode.PermanentRedirect,   // 308
        })
        {
            var handler = new ScriptedHandler()
                .EnqueueRedirectWithCode("https://github.com/start", "https://objects.githubusercontent.com/dest", code)
                .EnqueueOk("https://objects.githubusercontent.com/dest", "OK");
            using var http = new HttpClient(handler);

            using var resp = await ZapretInstaller.GetFollowingAllowedRedirectsAsync(http,
                "https://github.com/start", CancellationToken.None);

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }
    }
}

// Minimal scripted HttpMessageHandler — maps exact request URL → canned response.
// Tracks how many requests were made so tests can assert that a rejected URL was
// never actually contacted on the wire.
internal sealed class ScriptedHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _responses = new();
    public int RequestCount { get; private set; }

    public ScriptedHandler EnqueueRedirect(string from, string to)
        => EnqueueRedirectWithCode(from, to, HttpStatusCode.Found);

    public ScriptedHandler EnqueueRedirectWithCode(string from, string to, HttpStatusCode code)
    {
        _responses[from] = () =>
        {
            var r = new HttpResponseMessage(code);
            r.Headers.Location = new Uri(to);
            return r;
        };
        return this;
    }

    public ScriptedHandler EnqueueOk(string url, string body)
    {
        _responses[url] = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body),
        };
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        string url = request.RequestUri!.ToString();
        if (_responses.TryGetValue(url, out var factory))
            return Task.FromResult(factory());
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
