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
