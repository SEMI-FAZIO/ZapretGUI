using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace ZapretGUI.Services;

public sealed class LogStreamService : IDisposable
{
    public ObservableCollection<LogEntry> Buffer { get; } = new();
    public bool Paused { get; set; }
    public bool HasLiveProcess => _proc is { HasExited: false };

    private Process? _proc;

    // Hold the actual delegate instances so Detach can unsubscribe them.
    // The previous code did `OutputDataReceived -= null` which is a no-op:
    // C# event subtraction removes a *matching* delegate, and null never
    // matches the captured lambdas. As a result, every Attach leaked the
    // Process handle plus the event-target closure.
    private DataReceivedEventHandler? _outputHandler;
    private DataReceivedEventHandler? _errorHandler;
    private EventHandler? _exitedHandler;

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
            _outputHandler = (_, e) => { if (e.Data is not null) Push(LogLevel.Info, e.Data, LogSource.Winws); };
            _errorHandler  = (_, e) => { if (e.Data is not null) Push(LogLevel.Error, e.Data, LogSource.Winws); };
            _exitedHandler = (_, _) => Application.Current?.Dispatcher.BeginInvoke(() => ProcessStopped?.Invoke());

            process.OutputDataReceived += _outputHandler;
            process.ErrorDataReceived += _errorHandler;
            process.Exited += _exitedHandler;
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
            if (_outputHandler is not null) p.OutputDataReceived -= _outputHandler;
            if (_errorHandler  is not null) p.ErrorDataReceived  -= _errorHandler;
            if (_exitedHandler is not null) p.Exited             -= _exitedHandler;
        }
        catch { }
        _outputHandler = null;
        _errorHandler = null;
        _exitedHandler = null;

        // We took ownership of the Process in Attach (ServiceManager hands it
        // off and doesn't keep a reference). Releasing the underlying OS
        // handle is now our job — otherwise every strategy restart leaks one.
        try { p.Dispose(); } catch { }
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

    public void Dispose() => Detach();
}

public enum LogLevel { Info, Warning, Error }

public enum LogSource { Winws, Service }

public sealed record LogEntry(DateTime Timestamp, LogLevel Level, string Text, LogSource Source = LogSource.Winws);
