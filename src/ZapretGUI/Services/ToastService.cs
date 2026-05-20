using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace ZapretGUI.Services;

public enum ToastKind { Info, Success, Warning, Error }

public sealed class Toast
{
    public required string Message { get; init; }
    public ToastKind Kind { get; init; } = ToastKind.Info;
    public TimeSpan TimeToLive { get; init; } = TimeSpan.FromSeconds(5);
}

public sealed class ToastService
{
    private static readonly Lazy<ToastService> _instance = new(() => new ToastService());
    public static ToastService Instance => _instance.Value;

    public ObservableCollection<Toast> Active { get; } = new();

    public void Show(string message, ToastKind kind = ToastKind.Info, double seconds = 4)
    {
        var t = new Toast { Message = message, Kind = kind, TimeToLive = TimeSpan.FromSeconds(seconds) };

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            Active.Add(t);
            while (Active.Count > 4) Active.RemoveAt(0);

            var timer = new DispatcherTimer { Interval = t.TimeToLive };
            timer.Tick += (_, _) => { timer.Stop(); Active.Remove(t); };
            timer.Start();
        });
    }

    public void Info(string msg)    => Show(msg, ToastKind.Info);
    public void Success(string msg) => Show(msg, ToastKind.Success);
    public void Warn(string msg)    => Show(msg, ToastKind.Warning);
    public void Error(string msg)   => Show(msg, ToastKind.Error);
}
