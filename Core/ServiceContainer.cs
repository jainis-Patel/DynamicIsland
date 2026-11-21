namespace DynamicIsland.Core;

/// <summary>
/// Lightweight dependency container and deterministic <see cref="IDisposable"/> lifecycle manager
/// for all Dynamic Island services. Disposes registered services in reverse registration order on shutdown.
/// </summary>
public sealed class ServiceContainer : IDisposable
{
    private readonly Dictionary<Type, object> _services = new();
    private readonly List<object> _disposables = new();
    private readonly object _lock = new();
    private bool _disposed;

    public T Register<T>(T instance) where T : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _services[typeof(T)] = instance;
            if ((instance is IDisposable || instance is IAsyncDisposable) && !_disposables.Contains(instance))
            {
                _disposables.Add(instance);
            }
            return instance;
        }
    }

    public T Get<T>() where T : class
    {
        lock (_lock)
        {
            if (_services.TryGetValue(typeof(T), out var service) && service is T typed)
            {
                return typed;
            }
        }
        throw new InvalidOperationException($"Service of type {typeof(T).Name} is not registered.");
    }

    public bool TryGet<T>(out T? service) where T : class
    {
        lock (_lock)
        {
            if (_services.TryGetValue(typeof(T), out var raw) && raw is T typed)
            {
                service = typed;
                return true;
            }
        }
        service = null;
        return false;
    }

    public int Count
    {
        get { lock (_lock) return _services.Count; }
    }

    public void Dispose()
    {
        object[] toDispose;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            toDispose = _disposables.ToArray();
            _disposables.Clear();
            _services.Clear();
        }

        // Dispose in reverse order of registration so dependent services clean up before shared dependencies.
        for (int i = toDispose.Length - 1; i >= 0; i--)
        {
            var item = toDispose[i];
            try
            {
                if (item is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                else if (item is IAsyncDisposable asyncDisposable)
                {
                    Task.Run(() => asyncDisposable.DisposeAsync().AsTask()).GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                App.Log($"[ServiceContainer] Error disposing {item.GetType().Name}: {ex.Message}");
            }
        }
    }
}
