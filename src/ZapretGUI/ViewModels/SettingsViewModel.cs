using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using ZapretGUI.Helpers;
using ZapretGUI.Services;
using ZDefree.Core.Probing;

namespace ZapretGUI.ViewModels;

public sealed class SettingsViewModel : BaseViewModel
{
    private readonly ZapretController _ctrl;
    private readonly MainViewModel _root;

    public string ZapretRoot => _ctrl.ZapretRoot;
    public string LocalVersion => DetectLocalVersion();
    public string GuiVersion => "1.0.0";

    private string DetectLocalVersion()
    {
        try
        {
            string svc = Path.Combine(_ctrl.ZapretRoot, "service.bat");
            if (!File.Exists(svc)) return "—";
            foreach (var line in File.ReadAllLines(svc).Take(20))
            {
                int idx = line.IndexOf("LOCAL_VERSION=", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0) return line[(idx + "LOCAL_VERSION=".Length)..].Trim().Trim('"', '\'');
            }
        }
        catch { }
        return "—";
    }

    private string? _latestVersion;
    public string? LatestVersion { get => _latestVersion; private set { SetField(ref _latestVersion, value); OnPropertyChanged(nameof(VersionStatus)); OnPropertyChanged(nameof(UpdateAvailable)); } }

    public string VersionStatus
    {
        get
        {
            if (string.IsNullOrEmpty(LatestVersion)) return "—";
            return LatestVersion == LocalVersion ? "Установлена последняя версия" : $"Доступно обновление: {LatestVersion}";
        }
    }

    public bool UpdateAvailable => !string.IsNullOrEmpty(LatestVersion) && LatestVersion != LocalVersion;

    public bool AutoUpdate
    {
        get => _ctrl.Filters.IsAutoUpdateEnabled();
        set { _ctrl.Filters.SetAutoUpdate(value); OnPropertyChanged(); _root.RefreshStatus(); }
    }

    // Behavior
    public bool StartWithWindows
    {
        get => App.Settings.Current.StartWithWindows;
        set { App.Settings.Update(s => s.StartWithWindows = value); App.AutoStart.SetEnabled(value); OnPropertyChanged(); }
    }
    public bool StartMinimized
    {
        get => App.Settings.Current.StartMinimized;
        set { App.Settings.Update(s => s.StartMinimized = value); OnPropertyChanged(); App.AutoStart.Sync(App.Settings.Current.StartWithWindows); }
    }
    public bool AutoStartBypass
    {
        get => App.Settings.Current.AutoStartBypass;
        set { App.Settings.Update(s => s.AutoStartBypass = value); OnPropertyChanged(); }
    }
    public bool CaptureWinwsLogs
    {
        get => App.Settings.Current.CaptureWinwsLogs;
        set { App.Settings.Update(s => s.CaptureWinwsLogs = value); OnPropertyChanged(); }
    }

    // Theme
    public AppTheme[] Themes { get; } = [AppTheme.Dark, AppTheme.Light, AppTheme.System];
    public AppTheme Theme
    {
        get => App.Settings.Current.Theme;
        set
        {
            App.Settings.Update(s => s.Theme = value);
            App.Theme.Apply(value);
            OnPropertyChanged();
        }
    }

    // Language
    public AppLanguage[] Languages { get; } = [AppLanguage.Ru, AppLanguage.En, AppLanguage.System];
    public AppLanguage Language
    {
        get => App.Settings.Current.Language;
        set
        {
            App.Settings.Update(s => s.Language = value);
            App.Loc.Apply(value);
            OnPropertyChanged();
        }
    }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set { SetField(ref _isBusy, value); CommandManager.InvalidateRequerySuggested(); } }

    // ---- ISP detection (uses ZDefree.Core.Probing.IspDetector) ----
    private string? _ispIp;
    public string? IspIp { get => _ispIp; private set => SetField(ref _ispIp, value); }

    private string? _ispCountry;
    public string? IspCountry { get => _ispCountry; private set => SetField(ref _ispCountry, value); }

    private string? _ispAsn;
    public string? IspAsn { get => _ispAsn; private set => SetField(ref _ispAsn, value); }

    private string? _ispOrg;
    public string? IspOrg { get => _ispOrg; private set => SetField(ref _ispOrg, value); }

    private string? _ispCompatTag;
    public string? IspCompatTag { get => _ispCompatTag; private set => SetField(ref _ispCompatTag, value); }

    private string? _ispError;
    public string? IspError { get => _ispError; private set => SetField(ref _ispError, value); }

    private bool _isDetectingIsp;
    public bool IsDetectingIsp { get => _isDetectingIsp; private set { SetField(ref _isDetectingIsp, value); CommandManager.InvalidateRequerySuggested(); } }

    public bool HasIspInfo => !string.IsNullOrEmpty(IspIp);

    public ICommand DetectIspCommand { get; private set; } = null!;

    public ICommand CheckUpdateCommand { get; }
    public ICommand OpenReleaseCommand { get; }
    public ICommand OpenRepoCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand ChangePathCommand { get; }
    public ICommand OpenGuiRepoCommand { get; }
    public ICommand ExportBackupCommand { get; }
    public ICommand ImportBackupCommand { get; }

    public SettingsViewModel(ZapretController ctrl, MainViewModel root)
    {
        _ctrl = ctrl;
        _root = root;
        CheckUpdateCommand = new AsyncRelayCommand(CheckUpdateAsync, () => !IsBusy);
        OpenReleaseCommand = new RelayCommand(_ => OpenUrl("https://github.com/Flowseal/zapret-discord-youtube/releases/latest"));
        OpenRepoCommand    = new RelayCommand(_ => OpenUrl("https://github.com/Flowseal/zapret-discord-youtube"));
        OpenGuiRepoCommand = new RelayCommand(_ => OpenUrl("https://github.com/Flowseal/zapret-discord-youtube"));
        OpenFolderCommand  = new RelayCommand(_ => OpenUrl(_ctrl.ZapretRoot));
        OpenLogsCommand    = new RelayCommand(_ => OpenUrl(Path.GetTempPath()));
        ChangePathCommand  = new RelayCommand(_ => ChangePath());
        ExportBackupCommand = new RelayCommand(_ => ExportBackup());
        ImportBackupCommand = new RelayCommand(_ => ImportBackup());
        DetectIspCommand    = new AsyncRelayCommand(DetectIspAsync, () => !IsDetectingIsp);
    }

    private async Task DetectIspAsync()
    {
        IsDetectingIsp = true;
        IspError = null;
        try
        {
            using var det = new IspDetector();
            var info = await det.DetectAsync();
            IspIp        = info.Ip;
            IspCountry   = info.Country;
            IspAsn       = info.Asn;
            IspOrg       = info.OrgName;
            IspCompatTag = info.CompatTag;
            OnPropertyChanged(nameof(HasIspInfo));
        }
        catch (Exception ex)
        {
            IspError = ex.Message;
        }
        finally
        {
            IsDetectingIsp = false;
        }
    }

    private void ExportBackup()
    {
        var dlg = new SaveFileDialog
        {
            FileName = $"zapret-gui-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            Filter = "Backup archive|*.zip",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            new BackupService(_ctrl).Export(dlg.FileName);
            ToastService.Instance.Success($"Сохранено: {Path.GetFileName(dlg.FileName)}");
        }
        catch (Exception ex) { ToastService.Instance.Error("Backup: " + ex.Message); }
    }

    private void ImportBackup()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Backup archive|*.zip|All|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var res = new BackupService(_ctrl).Import(dlg.FileName);
            string msg = $"Восстановлено: {(res.RestoredSettings ? "settings.json + " : "")}{res.RestoredFiles.Count} файлов. Перезапустите ZapretGUI.";
            ToastService.Instance.Success(msg);
        }
        catch (Exception ex) { ToastService.Instance.Error("Restore: " + ex.Message); }
    }

    private void ChangePath()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Выберите папку с zapret",
            InitialDirectory = _ctrl.ZapretRoot,
        };
        if (dlg.ShowDialog() != true) return;
        if (!ZapretController.IsZapretRoot(dlg.FolderName)) return;
        App.Settings.Update(s => s.ZapretRoot = dlg.FolderName);
        App.Controller.Rebind(dlg.FolderName);
        OnPropertyChanged(nameof(ZapretRoot));
        OnPropertyChanged(nameof(LocalVersion));
        _root.RefreshStatus();
    }

    private async Task CheckUpdateAsync()
    {
        IsBusy = true;
        try { LatestVersion = await _ctrl.Lists.FetchLatestVersionAsync() ?? "не удалось"; }
        finally { IsBusy = false; }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}
