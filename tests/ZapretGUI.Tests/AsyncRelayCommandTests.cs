using Xunit;
using ZapretGUI.Helpers;

namespace ZapretGUI.Tests;

public class AsyncRelayCommandTests
{
    [Fact]
    public async Task Execute_SwallowsException_AndInvokesOnError()
    {
        // Before the fix: an unhandled exception in an async-void command
        // escaped to DispatcherUnhandledException and crashed the app.
        // Now: caught, onError invoked.
        var tcs = new TaskCompletionSource<Exception>();
        var cmd = new AsyncRelayCommand(
            execute: async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("boom");
            },
            onError: ex => tcs.TrySetResult(ex));

        cmd.Execute(null);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        Assert.Same(tcs.Task, completed);  // onError actually fired
        var caught = await tcs.Task;
        Assert.IsType<InvalidOperationException>(caught);
        Assert.Equal("boom", caught.Message);
    }

    [Fact]
    public async Task Execute_SynchronousThrow_StillCaught()
    {
        // Lambda that throws BEFORE returning a Task — exception is synchronous,
        // not inside a faulted Task. The try/catch must catch this too.
        var tcs = new TaskCompletionSource<Exception>();
        var cmd = new AsyncRelayCommand(
            execute: () => throw new InvalidOperationException("sync-boom"),
            onError: ex => tcs.TrySetResult(ex));

        cmd.Execute(null);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        Assert.Same(tcs.Task, completed);
        Assert.Equal("sync-boom", (await tcs.Task).Message);
    }

    [Fact]
    public async Task Execute_OperationCanceled_DoesNotInvokeOnError()
    {
        // Cancellation is normal flow — should not be surfaced as an error.
        bool onErrorCalled = false;
        var completed = new TaskCompletionSource();

        var cmd = new AsyncRelayCommand(
            execute: async () =>
            {
                await Task.Yield();
                try { throw new OperationCanceledException(); }
                finally { completed.TrySetResult(); }
            },
            onError: _ => onErrorCalled = true);

        cmd.Execute(null);

        await Task.WhenAny(completed.Task, Task.Delay(2000));
        // Give the catch a moment to run after the throw.
        await Task.Delay(100);
        Assert.False(onErrorCalled, "OperationCanceledException must not reach onError");
    }

    [Fact]
    public async Task Execute_OnException_FallsBackToGlobalErrorHandler()
    {
        // If no per-command onError, the global handler must still get the exception.
        var tcs = new TaskCompletionSource<Exception>();
        var previous = AsyncRelayCommand.GlobalErrorHandler;
        try
        {
            AsyncRelayCommand.GlobalErrorHandler = ex => tcs.TrySetResult(ex);

            var cmd = new AsyncRelayCommand(
                execute: async () => { await Task.Yield(); throw new InvalidOperationException("global-boom"); });

            cmd.Execute(null);

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(2000));
            Assert.Same(tcs.Task, completed);
            Assert.Equal("global-boom", (await tcs.Task).Message);
        }
        finally
        {
            AsyncRelayCommand.GlobalErrorHandler = previous;
        }
    }

    [Fact]
    public async Task Execute_HappyPath_NoErrorCallbacks()
    {
        bool ran = false;
        bool errored = false;
        var done = new TaskCompletionSource();

        var cmd = new AsyncRelayCommand(
            execute: async () => { await Task.Yield(); ran = true; done.TrySetResult(); },
            onError: _ => errored = true);

        cmd.Execute(null);

        await Task.WhenAny(done.Task, Task.Delay(2000));
        Assert.True(ran);
        Assert.False(errored);
    }
}
