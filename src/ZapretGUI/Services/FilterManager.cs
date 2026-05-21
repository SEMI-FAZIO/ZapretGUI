using System.IO;
using ZapretGUI.Models;

namespace ZapretGUI.Services;

public sealed class FilterManager
{
    private readonly string _root;

    public FilterManager(string zapretRoot) => _root = zapretRoot;

    private string GameFlagPath => Path.Combine(_root, "utils", "game_filter.enabled");
    private string AutoUpdateFlagPath => Path.Combine(_root, "utils", "check_updates.enabled");
    private string IpsetPath => Path.Combine(_root, "lists", "ipset-all.txt");
    private string IpsetBackupPath => Path.Combine(_root, "lists", "ipset-all.txt.backup");

    public (GameFilterMode mode, string allRange, string tcpRange, string udpRange) GetGameFilter()
    {
        const string off = "12";
        const string on = "1024-65535";

        if (!File.Exists(GameFlagPath))
            return (GameFilterMode.Disabled, off, off, off);

        string content = File.ReadAllText(GameFlagPath).Trim().ToLowerInvariant();

        return content switch
        {
            "all" => (GameFilterMode.All, on, on, on),
            "tcp" => (GameFilterMode.Tcp, on, on, off),
            "udp" => (GameFilterMode.Udp, on, off, on),
            _ => (GameFilterMode.Udp, on, off, on),
        };
    }

    /// <summary>
    /// Record-shaped view of <see cref="GetGameFilter"/> for <see cref="IStrategyProvider.ExtractWinwsArgs"/>.
    /// </summary>
    public GameFilterState GetGameFilterState()
    {
        var (mode, all, tcp, udp) = GetGameFilter();
        return new GameFilterState(mode, all, tcp, udp);
    }

    public void SetGameFilter(GameFilterMode mode)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(GameFlagPath)!);
        switch (mode)
        {
            case GameFilterMode.Disabled:
                if (File.Exists(GameFlagPath)) File.Delete(GameFlagPath);
                break;
            case GameFilterMode.All:
                File.WriteAllText(GameFlagPath, "all");
                break;
            case GameFilterMode.Tcp:
                File.WriteAllText(GameFlagPath, "tcp");
                break;
            case GameFilterMode.Udp:
                File.WriteAllText(GameFlagPath, "udp");
                break;
        }
    }

    public bool IsAutoUpdateEnabled() => File.Exists(AutoUpdateFlagPath);

    public void SetAutoUpdate(bool enabled)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AutoUpdateFlagPath)!);
        if (enabled)
            File.WriteAllText(AutoUpdateFlagPath, "ENABLED");
        else if (File.Exists(AutoUpdateFlagPath))
            File.Delete(AutoUpdateFlagPath);
    }

    public IpsetMode GetIpsetMode()
    {
        if (!File.Exists(IpsetPath)) return IpsetMode.Any;
        var info = new FileInfo(IpsetPath);
        if (info.Length == 0) return IpsetMode.Any;

        string content = File.ReadAllText(IpsetPath).Trim();
        if (content == "203.0.113.113/32") return IpsetMode.None;
        return IpsetMode.Loaded;
    }

    public void SetIpsetMode(IpsetMode mode)
    {
        var current = GetIpsetMode();
        if (current == mode) return;

        switch (mode)
        {
            case IpsetMode.None:
                if (current == IpsetMode.Loaded)
                {
                    if (File.Exists(IpsetBackupPath)) File.Delete(IpsetBackupPath);
                    if (File.Exists(IpsetPath)) File.Move(IpsetPath, IpsetBackupPath);
                }
                File.WriteAllText(IpsetPath, "203.0.113.113/32" + Environment.NewLine);
                break;

            case IpsetMode.Any:
                if (current == IpsetMode.Loaded)
                {
                    if (File.Exists(IpsetBackupPath)) File.Delete(IpsetBackupPath);
                    if (File.Exists(IpsetPath)) File.Move(IpsetPath, IpsetBackupPath);
                }
                File.WriteAllText(IpsetPath, string.Empty);
                break;

            case IpsetMode.Loaded:
                if (!File.Exists(IpsetBackupPath))
                    throw new InvalidOperationException("Резервная копия ipset-all.txt.backup отсутствует. Сначала обновите список через раздел «Списки».");
                if (File.Exists(IpsetPath)) File.Delete(IpsetPath);
                File.Move(IpsetBackupPath, IpsetPath);
                break;
        }
    }

    public void EnsureUserLists()
    {
        var listsDir = Path.Combine(_root, "lists");
        Directory.CreateDirectory(listsDir);

        WriteIfMissing(Path.Combine(listsDir, "ipset-exclude-user.txt"), "203.0.113.113/32");
        WriteIfMissing(Path.Combine(listsDir, "list-general-user.txt"), "domain.example.abc");
        WriteIfMissing(Path.Combine(listsDir, "list-exclude-user.txt"), "domain.example.abc");
    }

    private static void WriteIfMissing(string path, string content)
    {
        if (!File.Exists(path)) File.WriteAllText(path, content);
    }
}
