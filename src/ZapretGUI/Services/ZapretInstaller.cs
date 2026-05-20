using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace ZapretGUI.Services;

public sealed class ZapretInstaller
{
    public const string RepoApi = "https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest";
    public const string RepoUrl = "https://github.com/Flowseal/zapret-discord-youtube";

    public event Action<string>? Status;
    public event Action<int>? Progress;

    public async Task<string> InstallToAsync(string targetRoot, CancellationToken ct = default)
    {
        Directory.CreateDirectory(targetRoot);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ZapretGUI-Installer/1.0");
        http.Timeout = TimeSpan.FromMinutes(3);

        Report("Поиск последнего релиза…");
        var resp = await http.GetAsync(RepoApi, ct);
        resp.EnsureSuccessStatusCode();
        string body = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);

        string? zipUrl = null;
        string? tagName = null;
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
                    break;
                }
            }
        }
        if (zipUrl is null) throw new InvalidOperationException("Не нашли подходящий .zip в последнем релизе.");

        Report($"Скачивание {tagName ?? "latest"}…");
        string tempZip = Path.Combine(Path.GetTempPath(), $"zapret-{Guid.NewGuid():N}.zip");
        await DownloadAsync(http, zipUrl, tempZip, ct);

        Report("Распаковка…");
        string tempExtract = Path.Combine(Path.GetTempPath(), $"zapret-extract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempExtract);
        ZipFile.ExtractToDirectory(tempZip, tempExtract, overwriteFiles: true);

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
