using System.IO;
using System.Windows.Input;
using ZapretGUI.Helpers;
using ZapretGUI.Services;

namespace ZapretGUI.ViewModels;

public sealed class ListsViewModel : BaseViewModel
{
    private readonly ZapretController _ctrl;
    private readonly MainViewModel _root;

    private string _general = string.Empty;
    public string General { get => _general; set { if (SetField(ref _general, value)) IsDirtyGeneral = true; } }
    private bool _isDirtyGeneral;
    public bool IsDirtyGeneral { get => _isDirtyGeneral; private set => SetField(ref _isDirtyGeneral, value); }

    private string _exclude = string.Empty;
    public string Exclude { get => _exclude; set { if (SetField(ref _exclude, value)) IsDirtyExclude = true; } }
    private bool _isDirtyExclude;
    public bool IsDirtyExclude { get => _isDirtyExclude; private set => SetField(ref _isDirtyExclude, value); }

    private string _ipsetExclude = string.Empty;
    public string IpsetExclude { get => _ipsetExclude; set { if (SetField(ref _ipsetExclude, value)) IsDirtyIpsetExclude = true; } }
    private bool _isDirtyIpsetExclude;
    public bool IsDirtyIpsetExclude { get => _isDirtyIpsetExclude; private set => SetField(ref _isDirtyIpsetExclude, value); }

    private int _ipsetCount;
    public int IpsetCount { get => _ipsetCount; private set => SetField(ref _ipsetCount, value); }

    private string? _message;
    public string? Message { get => _message; set => SetField(ref _message, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set { SetField(ref _isBusy, value); CommandManager.InvalidateRequerySuggested(); } }

    public ICommand SaveGeneralCommand { get; }
    public ICommand SaveExcludeCommand { get; }
    public ICommand SaveIpsetExcludeCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand UpdateIpsetCommand { get; }
    public ICommand UpdateHostsCommand { get; }

    public ListsViewModel(ZapretController ctrl, MainViewModel root)
    {
        _ctrl = ctrl;
        _root = root;

        SaveGeneralCommand       = new RelayCommand(_ => Save(_ctrl.Lists.GeneralUserPath, General, () => IsDirtyGeneral = false));
        SaveExcludeCommand       = new RelayCommand(_ => Save(_ctrl.Lists.ExcludeUserPath, Exclude, () => IsDirtyExclude = false));
        SaveIpsetExcludeCommand  = new RelayCommand(_ => Save(_ctrl.Lists.IpsetExcludeUserPath, IpsetExclude, () => IsDirtyIpsetExclude = false));
        ReloadCommand            = new RelayCommand(_ => Reload());
        UpdateIpsetCommand       = new AsyncRelayCommand(UpdateIpsetAsync, () => !IsBusy);
        UpdateHostsCommand       = new AsyncRelayCommand(UpdateHostsAsync, () => !IsBusy);

        Reload();
    }

    public void Reload()
    {
        _ctrl.Filters.EnsureUserLists();
        General = _ctrl.Lists.ReadOrEmpty(_ctrl.Lists.GeneralUserPath);
        Exclude = _ctrl.Lists.ReadOrEmpty(_ctrl.Lists.ExcludeUserPath);
        IpsetExclude = _ctrl.Lists.ReadOrEmpty(_ctrl.Lists.IpsetExcludeUserPath);
        IpsetCount = _ctrl.Lists.CountLines(_ctrl.Lists.IpsetAllPath);
        IsDirtyGeneral = IsDirtyExclude = IsDirtyIpsetExclude = false;
        Message = null;
    }

    private void Save(string path, string content, Action onSaved)
    {
        try
        {
            _ctrl.Lists.Write(path, content);
            onSaved();
            Message = $"Сохранено: {Path.GetFileName(path)}";
        }
        catch (Exception ex) { Message = "Ошибка: " + ex.Message; }
    }

    private async Task UpdateIpsetAsync()
    {
        IsBusy = true;
        try
        {
            var (ok, msg) = await _ctrl.Lists.UpdateIpsetAsync();
            Message = msg;
            IpsetCount = _ctrl.Lists.CountLines(_ctrl.Lists.IpsetAllPath);
            _root.RefreshStatus();
        }
        finally { IsBusy = false; }
    }

    private async Task UpdateHostsAsync()
    {
        IsBusy = true;
        try
        {
            var (needs, tempFile, msg) = await _ctrl.Lists.CheckHostsAsync();
            Message = msg;
            if (needs && File.Exists(tempFile))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad", tempFile) { UseShellExecute = true });
                string hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer", hostsPath) { UseShellExecute = true });
            }
        }
        finally { IsBusy = false; }
    }
}
