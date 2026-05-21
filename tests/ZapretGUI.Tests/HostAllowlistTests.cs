using Xunit;
using ZapretGUI.Services;

namespace ZapretGUI.Tests;

public class HostAllowlistTests
{
    [Theory]
    [InlineData("https://github.com/Flowseal/zapret-discord-youtube/releases/download/v1/zapret.zip")]
    [InlineData("https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest")]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset/123?token=abc")]
    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/blob/foo")]
    [InlineData("https://GITHUB.COM/Flowseal/zapret-discord-youtube/releases/download/v1/zapret.zip")] // case-insensitive
    public void IsAllowedHost_AllowsKnownGitHubHosts(string url)
    {
        Assert.True(ZapretInstaller.IsAllowedHost(url), $"Expected {url} to pass");
    }

    [Theory]
    [InlineData("https://evil.example.com/zapret.zip")]
    [InlineData("https://github.io/zapret.zip")]
    // Lookalikes: exact host match required, suffix tricks must fail.
    [InlineData("https://attacker.github.com.evil.tld/zapret.zip")]
    [InlineData("https://github.com.attacker.tld/zapret.zip")]
    [InlineData("https://notgithub.com/zapret.zip")]
    [InlineData("http://github.com/zapret.zip")]  // http rejected, https only
    [InlineData("ftp://github.com/zapret.zip")]   // non-http scheme
    [InlineData("file:///C:/evil.zip")]            // local file
    public void IsAllowedHost_RejectsEverythingElse(string url)
    {
        Assert.False(ZapretInstaller.IsAllowedHost(url), $"Expected {url} to be rejected");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("javascript:alert(1)")]
    [InlineData("//github.com/zapret.zip")] // protocol-relative — TryCreate Absolute=false
    public void IsAllowedHost_RejectsMalformedInput(string url)
    {
        Assert.False(ZapretInstaller.IsAllowedHost(url));
    }
}
