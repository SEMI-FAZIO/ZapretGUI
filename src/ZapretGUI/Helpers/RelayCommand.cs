using System.Windows.Input;

namespace ZapretGUI.Helpers;

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = _ => execute();
        _canExecute = canExecute is null ? null : _ => canExecute();
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Predicate<object?>? _canExecute;
    private readonly Action<Exception>? _onError;
    private bool _isRunning;

    /// <summary>
    /// Global error handler invoked when any AsyncRelayCommand swallows an
    /// exception that the per-command <c>onError</c> did not handle.
    /// Wire this up once at app startup (e.g. App.OnStartup) to surface a
    /// toast or write to the log instead of letting the exception escape
    /// to DispatcherUnhandledException, which on async-void crashes the app.
    /// </summary>
    public static Action<Exception>? GlobalErrorHandler { get; set; }

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null, Action<Exception>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = _ => execute();
        _canExecute = canExecute is null ? null : _ => canExecute();
        _onError = onError;
    }

    public AsyncRelayCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null, Action<Exception>? onError = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _onError = onError;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _isRunning = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await _execute(parameter);
        }
        catch (OperationCanceledException)
        {
            // User-initiated cancellation is not an error.
        }
        catch (Exception ex)
        {
            // Swallow here so the exception does not propagate out of async void
            // (which would land in DispatcherUnhandledException and crash the
            // app). Surface via per-command handler first, then the global one.
            System.Diagnostics.Debug.WriteLine($"[AsyncRelayCommand] {ex}");
            try { _onError?.Invoke(ex); } catch { }
            try { GlobalErrorHandler?.Invoke(ex); } catch { }
        }
        finally
        {
            _isRunning = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
