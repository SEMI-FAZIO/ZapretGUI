using System.Collections.ObjectModel;
using System.Windows.Threading;
using ZapretGUI.Models;
using ZapretGUI.Services;
using ZapretGUI.Views;

namespace ZapretGUI.ViewModels;

public sealed class MainViewModel : BaseViewModel
{
    private readonly ZapretController _ctrl;
    private readonly DispatcherTimer _refreshTimer;

    public DashboardViewModel Dashboard { get; }
    public StrategiesViewModel Strategies { get; }
    public ListsViewModel Lists { get; }
    public DiagnosticsViewModel Diagnostics { get; }
    public LogsViewModel LogsVM { get; }
    public SettingsViewModel Settings { get; }

    public ObservableCollection<NavItem> NavItems { get; } = new();

    private NavItem? _selectedNav;
    public NavItem? SelectedNav
    {
        get => _selectedNav;
        set { if (SetField(ref _selectedNav, value) && value is not null) CurrentPage = value.Page; }
    }

    private object? _currentPage;
    public object? CurrentPage { get => _currentPage; private set => SetField(ref _currentPage, value); }

    private ZapretStatus _status = new();
    public ZapretStatus Status { get => _status; private set => SetField(ref _status, value); }

    public string StrategyDisplay
    {
        get
        {
            string label = LocalizationService.Get("Dashboard.CurrentStrategy").ToLower(LocalizationService.Instance.Culture);
            if (!string.IsNullOrWhiteSpace(Status.CurrentStrategy)) return $"{label}: {Status.CurrentStrategy}";
            if (Strategies.SelectedStrategy is not null) return $"{label}: {Strategies.SelectedStrategy.DisplayName}";
            return label;
        }
    }

    public string Version => "1.0.0";

    /// <summary>Active strategy provider name — "Flowseal" or "ZDefree".</summary>
    public string ProviderName => _ctrl.Strategies.ProviderName;

    public string ProviderLabel => LocalizationService.Get($"Mode.{ProviderName}");
    public string ProviderHint  => LocalizationService.Get($"Mode.Hint.{ProviderName}");

    /// <summary>True when running in native ZDefree mode (manifest.json detected).</summary>
    public bool IsZDefreeMode => ProviderName == "ZDefree";

    public MainViewModel()
    {
        _ctrl = App.Controller;

        Dashboard = new DashboardViewModel(_ctrl, this);
        Strategies = new StrategiesViewModel(_ctrl, this);
        Lists = new ListsViewModel(_ctrl, this);
        Diagnostics = new DiagnosticsViewModel(_ctrl);
        LogsVM = new LogsViewModel(App.Logs);
        Settings = new SettingsViewModel(_ctrl, this);

        Strategies.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(StrategiesViewModel.SelectedStrategy))
                OnPropertyChanged(nameof(StrategyDisplay));
        };

        BuildNav();
        LocalizationService.Instance.LanguageChanged += BuildNav;
        LocalizationService.Instance.LanguageChanged += () =>
        {
            OnPropertyChanged(nameof(ProviderLabel));
            OnPropertyChanged(nameof(ProviderHint));
        };
        _ctrl.RootChanged += () =>
        {
            OnPropertyChanged(nameof(ProviderName));
            OnPropertyChanged(nameof(ProviderLabel));
            OnPropertyChanged(nameof(ProviderHint));
            OnPropertyChanged(nameof(IsZDefreeMode));
        };

        SelectedNav = NavItems.FirstOrDefault();

        RefreshStatus();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += (_, _) => RefreshStatus();
        _refreshTimer.Start();
    }

    private void BuildNav()
    {
        int saved = NavItems.IndexOf(SelectedNav!);
        NavItems.Clear();
        NavItems.Add(new NavItem(LocalizationService.Get("Nav.Dashboard"),   "", new DashboardPage   { DataContext = Dashboard }));
        NavItems.Add(new NavItem(LocalizationService.Get("Nav.Strategies"),  "", new StrategiesPage  { DataContext = Strategies }));
        NavItems.Add(new NavItem(LocalizationService.Get("Nav.Lists"),       "", new ListsPage       { DataContext = Lists }));
        NavItems.Add(new NavItem(LocalizationService.Get("Nav.Diagnostics"), "", new DiagnosticsPage { DataContext = Diagnostics }));
        NavItems.Add(new NavItem(LocalizationService.Get("Nav.Logs"),        "", new LogsPage        { DataContext = LogsVM }));
        NavItems.Add(new NavItem(LocalizationService.Get("Nav.Settings"),    "", new SettingsPage    { DataContext = Settings }));
        if (saved >= 0 && saved < NavItems.Count) SelectedNav = NavItems[saved];
        else SelectedNav = NavItems[0];
        OnPropertyChanged(nameof(StrategyDisplay));
    }

    public void RefreshStatus()
    {
        Status = _ctrl.GetStatus();
        OnPropertyChanged(nameof(StrategyDisplay));
        Dashboard.OnStatusUpdated(Status);
    }
}

public sealed record NavItem(string Title, string Icon, object Page);
