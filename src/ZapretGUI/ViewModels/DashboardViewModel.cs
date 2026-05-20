using System.Windows;
using System.Windows.Input;
using ZapretGUI.Helpers;
using ZapretGUI.Models;
using ZapretGUI.Services;

namespace ZapretGUI.ViewModels;

public sealed class DashboardViewModel : BaseViewModel
{
    private readonly ZapretController _ctrl;
    private readonly MainViewModel _root;

    public DashboardViewModel(ZapretController ctrl, MainViewModel root)
    {
        _ctrl = ctrl;
        _root = root;

        StartCommand = new AsyncRelayCommand(StartAsync, () => !IsBusy && Status.RunState == BypassRunState.Stopped && _root.Strategies.SelectedStrategy is not null);
        StopCommand = new AsyncRelayCommand(StopAsync, () => !IsBusy && Status.RunState != BypassRunState.Stopped);
        InstallServiceCommand = new AsyncRelayCommand(InstallServiceAsync, () => !IsBusy && _root.Strategies.SelectedStrategy is not null);
        RemoveServiceCommand = new AsyncRelayCommand(RemoveServiceAsync, () => !IsBusy && Status.ServiceInstalled);
        ChooseStrategyCommand = new RelayCommand(_ => _root.SelectedNav = _root.NavItems[1]);

        LocalizationService.Instance.LanguageChanged += () =>
        {
            OnPropertyChanged(nameof(StatusBig));
            OnPropertyChanged(nameof(StatusSub));
            OnPropertyChanged(nameof(GameFilterText));
            OnPropertyChanged(nameof(IpsetText));
            OnPropertyChanged(nameof(ServiceText));
            OnPropertyChanged(nameof(WinwsText));
            OnPropertyChanged(nameof(WinDivertText));
            OnPropertyChanged(nameof(ServiceInstalledText));
            OnPropertyChanged(nameof(SelectedStrategyDisplay));
        };
    }

    private ZapretStatus _status = new();
    public ZapretStatus Status { get => _status; private set { SetField(ref _status, value); OnPropertyChanged(nameof(StatusBig)); OnPropertyChanged(nameof(StatusSub)); OnPropertyChanged(nameof(IsRunning)); OnPropertyChanged(nameof(GameFilterText)); OnPropertyChanged(nameof(IpsetText)); OnPropertyChanged(nameof(ServiceText)); OnPropertyChanged(nameof(WinwsText)); OnPropertyChanged(nameof(WinDivertText)); OnPropertyChanged(nameof(ServiceInstalledText)); } }

    public bool IsRunning => Status.RunState != BypassRunState.Stopped;

    public string StatusBig => Status.RunState == BypassRunState.Stopped
        ? LocalizationService.Get("Status.BypassDisabled")
        : LocalizationService.Get("Status.BypassActive");

    public string StatusSub => Status.RunState switch
    {
        BypassRunState.RunningAsService => LocalizationService.Get("Status.AsService"),
        BypassRunState.RunningStandalone => LocalizationService.Get("Status.AsStandalone"),
        _ => LocalizationService.Get("Status.PickStrategy"),
    };

    public string GameFilterText => LocalizationService.Get(Status.GameFilter switch
    {
        GameFilterMode.All => "Dashboard.GameFilter.All",
        GameFilterMode.Tcp => "Dashboard.GameFilter.Tcp",
        GameFilterMode.Udp => "Dashboard.GameFilter.Udp",
        _ => "Dashboard.GameFilter.Off",
    });

    public string IpsetText => LocalizationService.Get(Status.Ipset switch
    {
        IpsetMode.Loaded => "Dashboard.Ipset.Loaded",
        IpsetMode.Any => "Dashboard.Ipset.Any",
        IpsetMode.None => "Dashboard.Ipset.None",
        _ => "Dashboard.Ipset.None",
    });

    public string WinwsText => Status.WinwsRunning
        ? LocalizationService.Get("Status.Running.Service").Replace("(сервис)", "").Replace("(service)", "").Trim()
        : LocalizationService.Get("Status.Stopped");

    public string WinDivertText => Status.WinDivertActive
        ? LocalizationService.Get("Status.Running.Service").Replace("(сервис)", "").Replace("(service)", "").Trim()
        : LocalizationService.Get("Status.Stopped");

    public string ServiceInstalledText => Status.ServiceInstalled
        ? LocalizationService.Get("Dashboard.InstallService")
        : LocalizationService.Get("Status.Stopped");

    public string ServiceText
    {
        get
        {
            string installed = LocalizationService.Get("Dashboard.InstallService");
            string root = LocalizationService.Get("Dashboard.Service.Sub");
            if (!Status.ServiceInstalled) return $"{installed}: —";
            if (string.IsNullOrEmpty(Status.CurrentStrategy)) return $"{installed}: ✓";
            return $"{installed}: {Status.CurrentStrategy}";
        }
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            SetField(ref _isBusy, value);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private string? _lastMessage;
    public string? LastMessage { get => _lastMessage; private set => SetField(ref _lastMessage, value); }

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand InstallServiceCommand { get; }
    public ICommand RemoveServiceCommand { get; }
    public ICommand ChooseStrategyCommand { get; }

    public void OnStatusUpdated(ZapretStatus s) => Status = s;

    public Strategy? SelectedStrategy => _root.Strategies.SelectedStrategy;
    public string SelectedStrategyDisplay => SelectedStrategy?.DisplayName ?? LocalizationService.Get("Common.NotSelected");

    private async Task StartAsync()
    {
        var s = _root.Strategies.SelectedStrategy;
        if (s is null)
        {
            _root.SelectedNav = _root.NavItems[1];
            return;
        }

        IsBusy = true;
        try
        {
            bool capture = App.Settings.Current.CaptureWinwsLogs;
            LastMessage = await _ctrl.Services.RunStandaloneAsync(s, App.Logs, capture);
            App.Settings.Update(x => x.LastStrategy = s.FileName);
            ToastService.Instance.Success(LastMessage);
            _ = AutoTestAfterActivateAsync();
        }
        catch (Exception ex) { LastMessage = "Ошибка: " + ex.Message; ToastService.Instance.Error(ex.Message); }
        finally
        {
            IsBusy = false;
            _root.RefreshStatus();
        }
    }

    private async Task AutoTestAfterActivateAsync()
    {
        // Wait a bit for winws.exe to initialize WinDivert filters, then probe a few targets.
        await Task.Delay(2800);
        try
        {
            var checker = new ConnectivityChecker();
            var probes = checker.Build().Where(p => p.Host is "discord.com" or "youtube.com").ToArray();
            await Task.WhenAll(probes.Select(p => checker.ProbeAsync(p)));

            var ok = probes.Where(p => p.Status == ProbeStatus.Ok).ToList();
            var bad = probes.Where(p => p.Status == ProbeStatus.Fail).ToList();

            if (bad.Count == 0)
            {
                string detail = string.Join(", ", ok.Select(p => $"{p.Title} {p.HttpLatencyMs}ms"));
                ToastService.Instance.Success($"Авто-тест: всё доступно — {detail}");
            }
            else
            {
                string detail = string.Join(", ", bad.Select(p => $"{p.Title}: {p.Detail}"));
                ToastService.Instance.Warn($"Авто-тест: не доступно — {detail}. Попробуйте другую стратегию.");
            }
        }
        catch { }
    }

    private async Task StopAsync()
    {
        IsBusy = true;
        try
        {
            if (Status.ServiceInstalled)
            {
                LastMessage = await _ctrl.Services.RemoveServiceAsync();
            }
            else
            {
                LastMessage = await Task.Run(() => _ctrl.Services.StopStandalone());
            }
            ToastService.Instance.Success(LastMessage);
        }
        catch (Exception ex) { LastMessage = "Ошибка: " + ex.Message; ToastService.Instance.Error(ex.Message); }
        finally { IsBusy = false; _root.RefreshStatus(); }
    }

    private async Task InstallServiceAsync()
    {
        var s = _root.Strategies.SelectedStrategy;
        if (s is null) { _root.SelectedNav = _root.NavItems[1]; return; }

        IsBusy = true;
        try
        {
            LastMessage = await _ctrl.Services.InstallAsServiceAsync(s);
            App.Settings.Update(x => x.LastStrategy = s.FileName);
            ToastService.Instance.Success(LastMessage);
            _ = AutoTestAfterActivateAsync();
        }
        catch (Exception ex) { LastMessage = "Ошибка: " + ex.Message; ToastService.Instance.Error(ex.Message); }
        finally { IsBusy = false; _root.RefreshStatus(); }
    }

    private async Task RemoveServiceAsync()
    {
        if (!Controls.ConfirmDialog.Ask(Application.Current.MainWindow,
                "Confirm.RemoveService.Title", "Confirm.RemoveService.Text",
                confirmKey: "Dashboard.RemoveService", tone: Controls.ConfirmTone.Danger))
            return;

        IsBusy = true;
        try { LastMessage = await _ctrl.Services.RemoveServiceAsync(); ToastService.Instance.Success(LastMessage); }
        catch (Exception ex) { LastMessage = "Ошибка: " + ex.Message; ToastService.Instance.Error(ex.Message); }
        finally { IsBusy = false; _root.RefreshStatus(); }
    }

    public void SetGameFilter(GameFilterMode mode)
    {
        try
        {
            _ctrl.Filters.SetGameFilter(mode);
            _root.RefreshStatus();
            LastMessage = "Игровой фильтр обновлён. Перезапустите обход для применения.";
            ToastService.Instance.Info(LastMessage);
        }
        catch (Exception ex) { LastMessage = "Ошибка: " + ex.Message; ToastService.Instance.Error(ex.Message); }
    }

    public void SetIpset(IpsetMode mode)
    {
        try
        {
            _ctrl.Filters.SetIpsetMode(mode);
            _root.RefreshStatus();
            LastMessage = "IPSet режим переключён.";
            ToastService.Instance.Info(LastMessage);
        }
        catch (Exception ex) { LastMessage = "Ошибка: " + ex.Message; ToastService.Instance.Error(ex.Message); }
    }

    public void SetAutoUpdate(bool on)
    {
        try { _ctrl.Filters.SetAutoUpdate(on); _root.RefreshStatus(); LastMessage = "Авто-проверка обновлений: " + (on ? "включена" : "отключена"); }
        catch (Exception ex) { LastMessage = "Ошибка: " + ex.Message; }
    }
}
