using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using ZapretGUI.Helpers;
using ZapretGUI.Services;

namespace ZapretGUI.ViewModels;

public sealed class LogsViewModel : BaseViewModel
{
    private readonly LogStreamService _logs;
    private readonly ServiceEventLog _events = new();

    public ObservableCollection<LogEntry> Entries => _logs.Buffer;
    public ICollectionView View { get; }

    private bool _paused;
    public bool Paused { get => _paused; set { SetField(ref _paused, value); _logs.Paused = value; } }

    private bool _autoScroll = true;
    public bool AutoScroll { get => _autoScroll; set => SetField(ref _autoScroll, value); }

    private LogSource? _filterSource;
    public LogSource? FilterSource { get => _filterSource; set { if (SetField(ref _filterSource, value)) View.Refresh(); } }

    public event Action? NewEntryAdded;

    public ICommand ClearCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand LoadServiceEventsCommand { get; }
    public ICommand ShowAllCommand { get; }
    public ICommand ShowWinwsCommand { get; }
    public ICommand ShowServiceCommand { get; }

    public LogsViewModel(LogStreamService logs)
    {
        _logs = logs;
        View = CollectionViewSource.GetDefaultView(_logs.Buffer);
        View.Filter = o => o is LogEntry e && (FilterSource is null || e.Source == FilterSource);

        _logs.EntryAdded += _ => NewEntryAdded?.Invoke();
        ClearCommand              = new RelayCommand(_ => _logs.Clear());
        SaveCommand               = new RelayCommand(_ => SaveToFile());
        LoadServiceEventsCommand  = new AsyncRelayCommand(LoadServiceEventsAsync);
        ShowAllCommand            = new RelayCommand(_ => FilterSource = null);
        ShowWinwsCommand          = new RelayCommand(_ => FilterSource = LogSource.Winws);
        ShowServiceCommand        = new RelayCommand(_ => FilterSource = LogSource.Service);
    }

    private async Task LoadServiceEventsAsync()
    {
        var list = await Task.Run(() => _events.ReadRecent(maxRecords: 50));
        if (list.Count > 0)
        {
            _logs.PushBatch(list.OrderBy(x => x.Timestamp));
            ToastService.Instance.Info($"Загружено {list.Count} событий SCM");
        }
        else
        {
            ToastService.Instance.Info("Событий сервиса для zapret в System log пока нет.");
        }
    }

    private void SaveToFile()
    {
        var dlg = new SaveFileDialog
        {
            FileName = $"winws-{DateTime.Now:yyyyMMdd-HHmmss}.log",
            Filter = "Log file|*.log|All|*.*",
        };
        if (dlg.ShowDialog() == true)
            _logs.SaveToFile(dlg.FileName);
    }
}
