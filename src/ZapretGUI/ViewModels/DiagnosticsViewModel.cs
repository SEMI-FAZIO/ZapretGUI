using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using ZapretGUI.Controls;
using ZapretGUI.Helpers;
using ZapretGUI.Models;
using ZapretGUI.Services;

namespace ZapretGUI.ViewModels;

public sealed class DiagnosticsViewModel : BaseViewModel
{
    private readonly ZapretController _ctrl;
    private readonly ConnectivityChecker _conn = new();

    public ObservableCollection<DiagnosticResult> Results { get; } = new();
    public ObservableCollection<ProbeResult> Probes { get; }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set { SetField(ref _isBusy, value); CommandManager.InvalidateRequerySuggested(); OnPropertyChanged(nameof(IsIdle)); } }
    public bool IsIdle => !IsBusy;

    private bool _isProbing;
    public bool IsProbing { get => _isProbing; set { SetField(ref _isProbing, value); CommandManager.InvalidateRequerySuggested(); } }

    private string? _summary;
    public string? Summary { get => _summary; private set => SetField(ref _summary, value); }

    private string? _message;
    public string? Message { get => _message; private set => SetField(ref _message, value); }

    public ICommand RunCommand { get; }
    public ICommand ClearDiscordCacheCommand { get; }
    public ICommand RunTestsCommand { get; }
    public ICommand ConnectionTestCommand { get; }

    public DiagnosticsViewModel(ZapretController ctrl)
    {
        _ctrl = ctrl;
        Probes = new ObservableCollection<ProbeResult>(_conn.Build());

        RunCommand               = new AsyncRelayCommand(RunAsync, () => !IsBusy);
        ClearDiscordCacheCommand = new AsyncRelayCommand(ClearCacheAsync, () => !IsBusy);
        RunTestsCommand          = new RelayCommand(_ => RunPowerShellTests());
        ConnectionTestCommand    = new AsyncRelayCommand(RunConnectionTestAsync, () => !IsProbing);
    }

    private async Task RunAsync()
    {
        IsBusy = true;
        Results.Clear();
        Message = null;
        try
        {
            var list = await _ctrl.Diagnostics.RunAllAsync();
            foreach (var r in list) Results.Add(r);

            int ok = list.Count(x => x.Level == DiagnosticLevel.Ok);
            int warn = list.Count(x => x.Level == DiagnosticLevel.Warning);
            int err = list.Count(x => x.Level == DiagnosticLevel.Error);
            Summary = $"Проверено: {list.Count}. Ошибок: {err}, предупреждений: {warn}, OK: {ok}.";
        }
        finally { IsBusy = false; }
    }

    private async Task ClearCacheAsync()
    {
        if (!ConfirmDialog.Ask(Application.Current.MainWindow, "Confirm.ClearCache.Title", "Confirm.ClearCache.Text",
            confirmKey: "Diagnostics.ClearDiscord", tone: ConfirmTone.Warning))
            return;

        IsBusy = true;
        try
        {
            string msg = await Task.Run(_ctrl.Diagnostics.ClearDiscordCache);
            Message = msg;
        }
        finally { IsBusy = false; }
    }

    private void RunPowerShellTests()
    {
        try
        {
            string script = Path.Combine(_ctrl.ZapretRoot, "utils", "test zapret.ps1");
            if (!File.Exists(script))
            {
                Message = "Не найден файл utils\\test zapret.ps1";
                return;
            }
            var psi = new ProcessStartInfo("powershell", $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"")
            {
                UseShellExecute = true,
                WorkingDirectory = _ctrl.ZapretRoot,
            };
            Process.Start(psi);
            Message = "Тесты запущены в отдельном окне PowerShell.";
        }
        catch (Exception ex) { Message = "Ошибка: " + ex.Message; }
    }

    private async Task RunConnectionTestAsync()
    {
        IsProbing = true;
        try
        {
            foreach (var p in Probes) { p.Status = ProbeStatus.Pending; p.Detail = null; p.HttpLatencyMs = null; p.PingMs = null; }
            RefreshProbes();

            await Task.WhenAll(Probes.Select(p => _conn.ProbeAsync(p)));
            RefreshProbes();
        }
        finally { IsProbing = false; }
    }

    private void RefreshProbes()
    {
        var snap = Probes.ToList();
        Probes.Clear();
        foreach (var p in snap) Probes.Add(p);
    }
}
