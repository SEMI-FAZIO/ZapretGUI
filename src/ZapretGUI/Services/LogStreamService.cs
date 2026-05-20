using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace ZapretGUI.Services;

public sealed class LogStreamService
{
    public ObservableCollection<LogEntry> Buffer { get; } = new();
    public bool Paused { get; set; }
    public bool HasLiveProcess => _proc is { HasExited: false };

    private Process? _proc;
    private const int MaxLines = 5000;

    public event Action<LogEntry>? EntryAdded;
    public event Action? Cleared;
    public event Action? ProcessStopped;

    public void Attach(Process process)
    {
        Detach();
        _proc = process;
        try
        {
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) Push(LogLevel.Info, e.Data, LogSource.Winws); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Push(LogLevel.Error, e.Data, LogSource.Winws); };
            process.Exited += (_, _) => Application.Current?.Dispatcher.BeginInvoke(() => ProcessStopped?.Invoke());
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch { }
    }

    public void Detach()
    {
        var p = _proc;
        _proc = null;
        if (p is null) return;
        try
        {
            p.OutputDataReceived -= null;
            p.ErrorDataReceived -= null;
        }
        catch { }
    }

    public void Push(LogLevel level, string line, LogSource source = LogSource.Winws)
    {
        if (Paused && source == LogSource.Winws) return;
        var entry = new LogEntry(DateTime.Now, level, line, source);
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            Buffer.Add(entry);
            while (Buffer.Count > MaxLines) Buffer.RemoveAt(0);
            EntryAdded?.Invoke(entry);
        });
    }

    public void PushBatch(IEnumerable<LogEntry> entries)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            foreach (var e in entries)
            {
                Buffer.Add(e);
                EntryAdded?.Invoke(e);
            }
            while (Buffer.Count > MaxLines) Buffer.RemoveAt(0);
        });
    }

    public void Clear()
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            Buffer.Clear();
            Cleared?.Invoke();
        });
    }

    public void SaveToFile(string path)
    {
        using var sw = new StreamWriter(path, false);
        foreach (var e in Buffer)
            sw.WriteLine($"[{e.Timestamp:HH:mm:ss}] {e.Level,-5} {e.Text}");
    }
}

public enum LogLevel { Info, Warning, Error }

public enum LogSource { Winws, Service }

public sealed record LogEntry(DateTime Timestamp, LogLevel Level, string Text, LogSource Source = LogSource.Winws);
