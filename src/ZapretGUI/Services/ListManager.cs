using System.IO;
using System.Net.Http;
using System.Text;

namespace ZapretGUI.Services;

public sealed class ListManager
{
    public const string IpsetUrl = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/ipset-service.txt";
    public const string HostsUrl = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/hosts";

    private readonly string _root;
    private readonly HttpClient _http;

    public ListManager(string zapretRoot)
    {
        _root = zapretRoot;
        _http = new HttpClient(new HttpClientHandler { CheckCertificateRevocationList = true });
        _http.DefaultRequestHeaders.Add("User-Agent", "ZapretGUI/1.0");
        _http.Timeout = TimeSpan.FromSeconds(20);
    }

    public string ListsDir => Path.Combine(_root, "lists");
    public string GeneralUserPath => Path.Combine(ListsDir, "list-general-user.txt");
    public string ExcludeUserPath => Path.Combine(ListsDir, "list-exclude-user.txt");
    public string IpsetExcludeUserPath => Path.Combine(ListsDir, "ipset-exclude-user.txt");
    public string IpsetAllPath => Path.Combine(ListsDir, "ipset-all.txt");

    public string ReadOrEmpty(string path)
    {
        if (!File.Exists(path)) return string.Empty;
        return File.ReadAllText(path);
    }

    public void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    public int CountLines(string path)
    {
        if (!File.Exists(path)) return 0;
        return File.ReadLines(path).Count(l => !string.IsNullOrWhiteSpace(l));
    }

    public async Task<(bool ok, string message)> UpdateIpsetAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(IpsetUrl, ct);
            resp.EnsureSuccessStatusCode();
            string content = await resp.Content.ReadAsStringAsync(ct);
            Directory.CreateDirectory(Path.GetDirectoryName(IpsetAllPath)!);
            await File.WriteAllTextAsync(IpsetAllPath, content, new UTF8Encoding(false), ct);
            return (true, $"Список IPSet обновлён ({content.Split('\n').Length} строк).");
        }
        catch (Exception ex)
        {
            return (false, $"Не удалось обновить IPSet: {ex.Message}");
        }
    }

    public async Task<(bool needsUpdate, string tempFile, string message)> CheckHostsAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(HostsUrl, ct);
            resp.EnsureSuccessStatusCode();
            string remote = await resp.Content.ReadAsStringAsync(ct);

            string hostsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
            string local = File.Exists(hostsFile) ? await File.ReadAllTextAsync(hostsFile, ct) : "";

            var remoteLines = remote.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (remoteLines.Count == 0) return (false, "", "Файл с сервера пустой.");

            bool firstMissing = !local.Contains(remoteLines.First());
            bool lastMissing = !local.Contains(remoteLines.Last());

            string tempPath = Path.Combine(Path.GetTempPath(), "zapret_hosts.txt");
            await File.WriteAllTextAsync(tempPath, remote, new UTF8Encoding(false), ct);

            if (firstMissing || lastMissing)
                return (true, tempPath, "Обнаружены отличия. Откройте скачанный файл и системный hosts, затем перенесите содержимое вручную.");

            File.Delete(tempPath);
            return (false, "", "Файл hosts актуален.");
        }
        catch (Exception ex)
        {
            return (false, "", $"Ошибка проверки hosts: {ex.Message}");
        }
    }

    public async Task<string?> FetchLatestVersionAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync("https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/main/.service/version.txt", ct);
            resp.EnsureSuccessStatusCode();
            return (await resp.Content.ReadAsStringAsync(ct)).Trim();
        }
        catch
        {
            return null;
        }
    }
}
