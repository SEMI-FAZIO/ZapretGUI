using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace ZapretGUI.Services;

public sealed class ZapretInstaller
{
    public const string RepoApi = "https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest";
    public const string RepoUrl = "https://github.com/Flowseal/zapret-discord-youtube";

    // Only allow asset downloads from GitHub-controlled hosts. We validate the
    // host on every redirect hop (see GetFollowingAllowedRedirectsAsync) — not
    // just on the initial URL, because the default HttpClient would silently
    // follow a 302 from an allowed host to any host and defeat the allowlist.
    internal static readonly string[] AllowedDownloadHosts =
    {
        "github.com",
        "api.github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
    };

    private const int MaxRedirects = 5;

    public event Action<string>? Status;
    public event Action<int>? Progress;

    public async Task<string> InstallToAsync(string targetRoot, CancellationToken ct = default)
    {
        Directory.CreateDirectory(targetRoot);

        var handler = new HttpClientHandler
        {
            CheckCertificateRevocationList = true,
            AllowAutoRedirect = false, // we follow redirects manually, see GetFollowingAllowedRedirectsAsync
        };
        using var http = new HttpClient(handler);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ZapretGUI-Installer/1.0");
        http.Timeout = TimeSpan.FromMinutes(3);

        Report("Поиск последнего релиза…");
        string body;
        using (var apiResp = await GetFollowingAllowedRedirectsAsync(http, RepoApi, ct))
        {
            body = await apiResp.Content.ReadAsStringAsync(ct);
        }
        using var doc = JsonDocument.Parse(body);

        string? zipUrl = null;
        string? tagName = null;
        string? expectedSha256 = null;
        if (doc.RootElement.TryGetProperty("tag_name", out var t)) tagName = t.GetString();
        if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assets.EnumerateArray())
            {
                if (!a.TryGetProperty("name", out var name)) continue;
                string? n = name.GetString();
                if (n is null) continue;
                if (n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                    n.Contains("zapret", StringComparison.OrdinalIgnoreCase))
                {
                    if (a.TryGetProperty("browser_download_url", out var url))
                        zipUrl = url.GetString();
                    // GitHub Releases API exposes a "digest" field on assets
                    // formatted as "sha256:HEX". Use it if available; older
                    // releases without one fall through to TLS-only.
                    if (a.TryGetProperty("digest", out var dig))
                    {
                        string? raw = dig.GetString();
                        if (!string.IsNullOrEmpty(raw) && raw.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                            expectedSha256 = raw.Substring("sha256:".Length).Trim();
                    }
                    break;
                }
            }
        }
        if (zipUrl is null) throw new InvalidOperationException("Не нашли подходящий .zip в последнем релизе.");

        Report($"Скачивание {tagName ?? "latest"}…");
        string tempZip = Path.Combine(Path.GetTempPath(), $"zapret-{Guid.NewGuid():N}.zip");
        using (var downloadResp = await GetFollowingAllowedRedirectsAsync(http, zipUrl, ct))
        {
            await StreamToFileAsync(downloadResp, tempZip, ct);
        }

        if (expectedSha256 is not null)
        {
            Report("Проверка целостности…");
            string actual = await ComputeSha256Async(tempZip, ct);
            if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(tempZip); } catch { }
                throw new InvalidOperationException(
                    $"SHA-256 не совпадает (ожидался {expectedSha256[..12]}…, получили {actual[..12]}…). " +
                    "Скачанный архив отброшен.");
            }
        }

        Report("Распаковка…");
        string tempExtract = Path.Combine(Path.GetTempPath(), $"zapret-extract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempExtract);
        SafeExtractToDirectory(tempZip, tempExtract);

        string srcRoot = LocateZapretRoot(tempExtract)
            ?? throw new InvalidOperationException("В архиве не найдено winws.exe + lists/.");

        Report("Копирование файлов…");
        CopyDirectory(srcRoot, targetRoot, overwrite: true);

        try { File.Delete(tempZip); } catch { }
        try { Directory.Delete(tempExtract, true); } catch { }

        Report("Готово.");
        Progress?.Invoke(100);
        return tagName ?? "latest";
    }

    // Follows HTTP redirects manually, validating EVERY hop's host against
    // AllowedDownloadHosts before issuing the request. With AllowAutoRedirect=true
    // the default HttpClient would silently follow a 302 from github.com to any
    // attacker host, defeating the allowlist.
    internal static async Task<HttpResponseMessage> GetFollowingAllowedRedirectsAsync(
        HttpClient http, string initialUrl, CancellationToken ct, int maxRedirects = MaxRedirects)
    {
        string currentUrl = initialUrl;
        HttpResponseMessage? resp = null;
        for (int hop = 0; hop <= maxRedirects; hop++)
        {
            if (!IsAllowedHost(currentUrl))
            {
                resp?.Dispose();
                throw new InvalidOperationException(
                    $"Отказано: загрузка с {SafeHost(currentUrl)} запрещена.");
            }

            resp?.Dispose();
            resp = await http.GetAsync(currentUrl, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!IsRedirect(resp.StatusCode))
            {
                resp.EnsureSuccessStatusCode();
                return resp;
            }

            var loc = resp.Headers.Location;
            if (loc is null)
            {
                resp.EnsureSuccessStatusCode();
                return resp;
            }

            var nextUri = loc.IsAbsoluteUri ? loc : new Uri(new Uri(currentUrl), loc);
            currentUrl = nextUri.ToString();
        }
        resp?.Dispose();
        throw new InvalidOperationException(
            $"Слишком много редиректов (>{maxRedirects}) при загрузке.");
    }

    internal static bool IsRedirect(HttpStatusCode code) =>
        code == HttpStatusCode.MovedPermanently    // 301
        || code == HttpStatusCode.Found             // 302
        || code == HttpStatusCode.SeeOther          // 303
        || code == HttpStatusCode.TemporaryRedirect // 307
        || code == HttpStatusCode.PermanentRedirect; // 308

    internal static bool IsAllowedHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps) return false;
        foreach (var host in AllowedDownloadHosts)
        {
            if (string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string SafeHost(string url)
    {
        try { return new Uri(url).Host; } catch { return "<invalid>"; }
    }

    internal static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        byte[] hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash);
    }

    // Iterates entries with traversal validation to prevent Zip-Slip
    // (an entry like "../../foo" escaping the target directory).
    internal static void SafeExtractToDirectory(string zipPath, string destRoot)
    {
        string normalizedRoot = Path.GetFullPath(destRoot);
        if (!normalizedRoot.EndsWith(Path.DirectorySeparatorChar)) normalizedRoot += Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            string normalizedName = entry.FullName.Replace('\\', '/').TrimStart('/');
            string target = Path.GetFullPath(Path.Combine(destRoot, normalizedName));
            if (!target.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Небезопасный путь в архиве: {entry.FullName}");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private async Task StreamToFileAsync(HttpResponseMessage resp, string destPath, CancellationToken ct)
    {
        long? total = resp.Content.Headers.ContentLength;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(destPath);
        var buf = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            read += n;
            if (total > 0)
            {
                int pct = (int)Math.Min(100, read * 100 / total.Value);
                Progress?.Invoke(pct);
            }
        }
    }

    private static string? LocateZapretRoot(string dir)
    {
        if (HasZapret(dir)) return dir;
        foreach (var sub in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories))
            if (HasZapret(sub)) return sub;
        return null;
    }

    private static bool HasZapret(string dir)
        => File.Exists(Path.Combine(dir, "bin", "winws.exe")) &&
           Directory.Exists(Path.Combine(dir, "lists"));

    private static void CopyDirectory(string src, string dst, bool overwrite)
    {
        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(src, dir);
            Directory.CreateDirectory(Path.Combine(dst, rel));
        }
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(src, file);
            string target = Path.Combine(dst, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite);
        }
    }

    private void Report(string msg) => Status?.Invoke(msg);
}
