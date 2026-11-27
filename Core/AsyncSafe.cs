namespace DynamicIsland.Core;

/// <summary>
/// Safe async fire-and-forget and UI event-handler wrappers that prevent unobserved task exceptions
/// or unhandled <c>async void</c> crashes from terminating the WPF Dispatcher.
/// </summary>
internal static class AsyncSafe
{
    public static void FireAndForget(this Task task, string context)
    {
        if (task == null) return;
        if (task.IsCompletedSuccessfully) return;

        _ = task.ContinueWith(
            static (t, state) =>
            {
                var ex = t.Exception?.GetBaseException();
                if (ex is null or OperationCanceledException) return;
                App.Log($"[AsyncSafe:{state}] {ex.GetType().Name}: {ex.Message}");
            },
            context,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}

/// <summary>
/// Non-reentrant single-flight gate for async UI click handlers so rapid double-clicks
/// never launch overlapping OS/network operations, and exceptions are always observed.
/// </summary>
internal sealed class SingleFlightGate
{
    private int _busy;

    public bool IsBusy => Volatile.Read(ref _busy) == 1;

    public Task<bool> TryRunAsync(Func<Task> work, string context)
    {
        if (work == null) return Task.FromResult(false);
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return Task.FromResult(false);

        return ExecuteCoreAsync(work, context);
    }

    private async Task<bool> ExecuteCoreAsync(Func<Task> work, string context)
    {
        try
        {
            await work().ConfigureAwait(true);
            return true;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[AsyncSafe:{context}] {ex.GetType().Name}: {ex.Message}");
            return true;
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }
}
