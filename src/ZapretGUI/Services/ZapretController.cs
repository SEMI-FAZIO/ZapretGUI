using System.IO;
using ZapretGUI.Models;

namespace ZapretGUI.Services;

public sealed class ZapretController
{
    public string ZapretRoot { get; private set; }
    public FilterManager Filters { get; private set; }
    public IStrategyProvider Strategies { get; private set; }
    public ServiceManager Services { get; private set; }
    public ListManager Lists { get; private set; }
    public DiagnosticsRunner Diagnostics { get; private set; }
    public bool IsZapretInstalled { get; private set; }

    public event Action? RootChanged;

    public ZapretController(string zapretRoot)
    {
        ZapretRoot = zapretRoot;
        Filters = new FilterManager(zapretRoot);
        Strategies = SelectProvider(zapretRoot);
        Services = new ServiceManager(zapretRoot, Filters, Strategies);
        Lists = new ListManager(zapretRoot);
        Diagnostics = new DiagnosticsRunner(zapretRoot);
        IsZapretInstalled = IsZapretRoot(zapretRoot);
    }

    public void Rebind(string newRoot)
    {
        ZapretRoot = newRoot;
        Filters = new FilterManager(newRoot);
        Strategies = SelectProvider(newRoot);
        Services = new ServiceManager(newRoot, Filters, Strategies);
        Lists = new ListManager(newRoot);
        Diagnostics = new DiagnosticsRunner(newRoot);
        IsZapretInstalled = IsZapretRoot(newRoot);
        RootChanged?.Invoke();
    }

    /// <summary>
    /// Picks the strategy provider for a given install root. <c>manifest.json</c>
    /// presence wins over <c>.bat</c> files — if both exist (mid-migration
    /// install), ZDefree mode is preferred.
    /// </summary>
    private static IStrategyProvider SelectProvider(string root)
    {
        if (IsZDefreeRoot(root))
        {
            // Read SettingsService directly (it's IO-only, no WPF dependency)
            // rather than LocalizationService — the latter touches
            // Application.Current.Resources and would fail outside a WPF app
            // context (e.g. unit tests, smoke tools).
            string lang = SettingsService.Instance.Current.Language == AppLanguage.Ru ? "ru" : "en";
            return new ZDefreeStrategyProvider(root, lang);
        }
        return new StrategyRepository(root);
    }

    /// <summary>
    /// True when the directory contains a ZDefree <c>manifest.json</c> at the
    /// root — the marker that this install is a ZDefree distribution.
    /// </summary>
    public static bool IsZDefreeRoot(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return false;
        try { return File.Exists(Path.Combine(dir, "manifest.json")); }
        catch { return false; }
    }

    public ZapretStatus GetStatus()
    {
        if (!IsZapretInstalled) return new ZapretStatus();

        var (mode, _, _, _) = Filters.GetGameFilter();
        var run = Services.IsServiceRunning()
            ? BypassRunState.RunningAsService
            : (Services.IsWinwsProcessRunning() ? BypassRunState.RunningStandalone : BypassRunState.Stopped);

        return new ZapretStatus
        {
            RunState = run,
            CurrentStrategy = Services.GetInstalledStrategyName(),
            ServiceInstalled = Services.IsServiceInstalled(),
            WinDivertActive = Services.IsWinDivertActive(),
            WinwsRunning = Services.IsWinwsProcessRunning(),
            GameFilter = mode,
            Ipset = Filters.GetIpsetMode(),
            AutoUpdateEnabled = Filters.IsAutoUpdateEnabled(),
        };
    }

    public static string ResolveInitialRoot()
    {
        var saved = SettingsService.Instance.Current.ZapretRoot;
        if (!string.IsNullOrEmpty(saved) && IsZapretRoot(saved)) return saved;

        string exeDir = AppContext.BaseDirectory;
        var current = new DirectoryInfo(exeDir);
        while (current is not null)
        {
            if (IsZapretRoot(current.FullName)) return current.FullName;
            current = current.Parent;
        }

        return exeDir;
    }

    public static bool IsZapretRoot(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return false;
        try
        {
            return Directory.Exists(Path.Combine(dir, "bin")) &&
                   Directory.Exists(Path.Combine(dir, "lists")) &&
                   File.Exists(Path.Combine(dir, "bin", "winws.exe"));
        }
        catch { return false; }
    }
}
