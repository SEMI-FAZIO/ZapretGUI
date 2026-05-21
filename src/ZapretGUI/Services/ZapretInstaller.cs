using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace ZapretGUI.Services;

public sealed class ZapretInstaller
{
    public const string RepoApi = "https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest";
    public const string RepoUrl = "https://github.com/Flowseal/zapret-discord-youtube";

    // Only allow asset downloads from GitHub-controlled hosts. Defense in depth
    // against an attacker who could manipulate the JSON we get back from the API.
    private static readonly string[] AllowedDownloadHosts =
    {
        "github.com",
        "api.github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
    };

    public event Action<string>? Status;
    public event Action<int>? Progress;

    public async Task<string> InstallToAsync(string targetRoot, CancellationToken ct = default)
    {
        Directory.CreateDirectory(targetRoot);

        using var http = new HttpClient(new HttpClientHandler { CheckCertificateRevocationList = true });
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ZapretGUI-Installer/1.0");
        http.Timeout = TimeSpan.FromMinutes(3);

        Report("Поиск последнего релиза…");
        var resp = await http.GetAsync(RepoApi, ct);
        resp.EnsureSuccessStatusCode();
        string body = await resp.Content.ReadAsStringAsync(ct);
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
                    // GitHub Releases API now exposes a "digest" field on assets
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
        if (!IsAllowedHost(zipUrl))
            throw new InvalidOperationException($"Отказано: загрузка с {new Uri(zipUrl).Host} запрещена.");

        Report($"Скачивание {tagName ?? "latest"}…");
        string tempZip = Path.Combine(Path.GetTempPath(), $"zapret-{Guid.NewGuid():N}.zip");
        await DownloadAsync(http, zipUrl, tempZip, ct);

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

    private static bool IsAllowedHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps) return false;
        foreach (var host in AllowedDownloadHosts)
        {
            if (string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        byte[] hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash);
    }

    // Iterates entries with traversal validation to prevent Zip-Slip
    // (an entry like "../../foo" escaping the target directory).
    private static void SafeExtractToDirectory(string zipPath, string destRoot)
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

    private async Task DownloadAsync(HttpClient http, string url, string destPath, CancellationToken ct)
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
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
