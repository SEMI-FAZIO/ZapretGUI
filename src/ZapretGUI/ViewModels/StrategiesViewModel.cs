using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Input;
using ZapretGUI.Helpers;
using ZapretGUI.Models;
using ZapretGUI.Services;
using ZDefree.Core.Probing;

namespace ZapretGUI.ViewModels;

public sealed class StrategiesViewModel : BaseViewModel
{
    private readonly ZapretController _ctrl;
    private readonly MainViewModel _root;

    public ObservableCollection<Strategy> All { get; } = new();
    public ICollectionView Filtered { get; }

    private Strategy? _selectedStrategy;
    public Strategy? SelectedStrategy
    {
        get => _selectedStrategy;
        set
        {
            if (SetField(ref _selectedStrategy, value))
            {
                OnPropertyChanged(nameof(PreviewArgs));
                OnPropertyChanged(nameof(CanRun));
                Diff = null;
                OnPropertyChanged(nameof(DiffSummary));
            }
        }
    }

    private string _search = string.Empty;
    public string Search
    {
        get => _search;
        set { if (SetField(ref _search, value)) Filtered.Refresh(); }
    }

    private string? _filterCategory;
    public string? FilterCategory
    {
        get => _filterCategory;
        set { if (SetField(ref _filterCategory, value)) Filtered.Refresh(); }
    }

    public ObservableCollection<string> Categories { get; } = new();

    public string PreviewArgs
    {
        get
        {
            if (SelectedStrategy is null) return "Выберите стратегию из списка слева, чтобы увидеть параметры winws.exe.";
            try
            {
                string args = _ctrl.Strategies.ExtractWinwsArgs(SelectedStrategy, _ctrl.Filters.GetGameFilterState());
                return PrettyPrintArgs(args);
            }
            catch (Exception ex)
            {
                return "Не удалось разобрать стратегию: " + ex.Message;
            }
        }
    }

    public bool CanRun => SelectedStrategy is not null;

    private System.Collections.ObjectModel.ObservableCollection<StrategyDiffLine>? _diff;
    public System.Collections.ObjectModel.ObservableCollection<StrategyDiffLine>? Diff { get => _diff; private set => SetField(ref _diff, value); }

    public string DiffSummary
    {
        get
        {
            if (Diff is null) return "";
            int changes = StrategyDiffEngine.CountDifferences(Diff);
            return changes == 0
                ? "Идентично «general.bat»"
                : $"Отличий от «general»: {changes}";
        }
    }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set { SetField(ref _isBusy, value); CommandManager.InvalidateRequerySuggested(); } }

    private string? _message;
    public string? Message { get => _message; set => SetField(ref _message, value); }

    public ICommand RunCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand ToggleDiffCommand { get; }
    public ICommand AutoPickCommand { get; }

    /// <summary>Visible/enabled only in ZDefree mode — Flowseal has no INDEX.json to rank.</summary>
    public bool CanAutoPick => _ctrl.Strategies.ProviderName == "ZDefree";

    private bool _isAutoPicking;
    public bool IsAutoPicking { get => _isAutoPicking; private set { SetField(ref _isAutoPicking, value); CommandManager.InvalidateRequerySuggested(); } }

    public StrategiesViewModel(ZapretController ctrl, MainViewModel root)
    {
        _ctrl = ctrl;
        _root = root;

        Filtered = CollectionViewSource.GetDefaultView(All);
        Filtered.Filter = FilterPredicate;

        RunCommand = new AsyncRelayCommand(RunAsync, () => !IsBusy && CanRun);
        InstallCommand = new AsyncRelayCommand(InstallAsync, () => !IsBusy && CanRun);
        RefreshCommand = new RelayCommand(_ => Reload());
        OpenFolderCommand = new RelayCommand(_ => OpenZapretFolder());
        ToggleDiffCommand = new RelayCommand(_ => ToggleDiff());
        AutoPickCommand = new AsyncRelayCommand(AutoPickAsync, () => !IsBusy && !IsAutoPicking && CanAutoPick);

        Reload();
    }

    private async Task AutoPickAsync()
    {
        IsAutoPicking = true;
        Message = null;
        try
        {
            string strategiesDir = Path.Combine(_ctrl.ZapretRoot, "strategies");
            using var ispDet = new IspDetector();
            var picker = new StrategyPicker(ispDet);
            var result = await picker.RunAsync(new PickerOptions
            {
                StrategiesDir = strategiesDir,
                IspMode       = "auto",
                DryRun        = true,
            });

            if (result.Candidates.Count == 0)
            {
                Message = LocalizationService.Get("Strategies.AutoPick.NoCandidates");
                return;
            }

            // Pick the top-ranked candidate (ISP-matched first, then by category/id).
            var winner = result.Candidates[0];
            var match = All.FirstOrDefault(s =>
                Path.GetFileNameWithoutExtension(s.FileName)
                    .Equals(winner.Id, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                Message = $"Auto-pick: top candidate '{winner.Id}' not found in catalog.";
                return;
            }

            SelectedStrategy = match;
            string ispLabel = result.DetectedIsp?.CompatTag ?? result.DetectedIsp?.Asn ?? "—";
            Message = winner.IspMatched
                ? string.Format(LocalizationService.Get("Strategies.AutoPick.PickedMatched"), winner.Name, ispLabel)
                : string.Format(LocalizationService.Get("Strategies.AutoPick.PickedDefault"), winner.Name);
            ToastService.Instance.Success(Message);
        }
        catch (Exception ex)
        {
            Message = "Auto-pick: " + ex.Message;
            ToastService.Instance.Error(Message);
        }
        finally
        {
            IsAutoPicking = false;
        }
    }

    private void ToggleDiff()
    {
        if (SelectedStrategy is null) return;
        if (Diff is not null) { Diff = null; OnPropertyChanged(nameof(DiffSummary)); return; }
        var baseStrat = StrategyDiffEngine.FindBaseStrategy(All);
        if (baseStrat is null || baseStrat.FullPath == SelectedStrategy.FullPath)
        {
            ToastService.Instance.Info("Сравнивать с самим general.bat нечего.");
            return;
        }
        try
        {
            var diff = StrategyDiffEngine.Diff(baseStrat, SelectedStrategy, _ctrl.Filters, _ctrl.ZapretRoot);
            // Только отличия — Same пропускаем для краткости
            var filtered = diff.Where(d => d.Kind != DiffKind.Same).ToList();
            Diff = new System.Collections.ObjectModel.ObservableCollection<StrategyDiffLine>(filtered);
            OnPropertyChanged(nameof(DiffSummary));
        }
        catch (Exception ex)
        {
            ToastService.Instance.Error("Не удалось сравнить: " + ex.Message);
        }
    }

    public void Reload()
    {
        var list = _ctrl.Strategies.Discover();
        All.Clear();
        Categories.Clear();
        Categories.Add("ВСЕ");
        foreach (var c in list.Select(s => s.Category ?? "GENERAL").Distinct().OrderBy(x => x))
            Categories.Add(c);
        foreach (var s in list) All.Add(s);

        string? installedName = _ctrl.Services.GetInstalledStrategyName();
        if (!string.IsNullOrEmpty(installedName))
        {
            var match = All.FirstOrDefault(s => Path.GetFileNameWithoutExtension(s.FileName).Equals(installedName, StringComparison.OrdinalIgnoreCase));
            if (match is not null) SelectedStrategy = match;
        }

        // Fall back to the last strategy saved in settings.
        if (SelectedStrategy is null)
        {
            var saved = App.Settings.Current.LastStrategy;
            if (!string.IsNullOrEmpty(saved))
            {
                var match = All.FirstOrDefault(s => s.FileName.Equals(saved, StringComparison.OrdinalIgnoreCase));
                if (match is not null) SelectedStrategy = match;
            }
        }
        SelectedStrategy ??= All.FirstOrDefault();
        FilterCategory = Categories.FirstOrDefault();
    }

    private bool FilterPredicate(object item)
    {
        if (item is not Strategy s) return false;
        if (!string.IsNullOrWhiteSpace(Search) && !s.DisplayName.Contains(Search, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(FilterCategory) && FilterCategory != "ВСЕ" && s.Category != FilterCategory) return false;
        return true;
    }

    private async Task RunAsync()
    {
        if (SelectedStrategy is null) return;
        IsBusy = true;
        try
        {
            bool capture = App.Settings.Current.CaptureWinwsLogs;
            Message = await _ctrl.Services.RunStandaloneAsync(SelectedStrategy, App.Logs, capture);
            App.Settings.Update(x => x.LastStrategy = SelectedStrategy.FileName);
        }
        catch (Exception ex) { Message = "Ошибка: " + ex.Message; }
        finally { IsBusy = false; _root.RefreshStatus(); }
    }

    private async Task InstallAsync()
    {
        if (SelectedStrategy is null) return;
        IsBusy = true;
        try { Message = await _ctrl.Services.InstallAsServiceAsync(SelectedStrategy); App.Settings.Update(x => x.LastStrategy = SelectedStrategy.FileName); }
        catch (Exception ex) { Message = "Ошибка: " + ex.Message; }
        finally { IsBusy = false; _root.RefreshStatus(); }
    }

    private void OpenZapretFolder()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _ctrl.ZapretRoot,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { Message = "Ошибка: " + ex.Message; }
    }

    private static string PrettyPrintArgs(string args)
    {
        return args.Replace(" --", "\n--");
    }
}
