namespace DynamicIsland.Core;

/// <summary>
/// Lightweight dependency container and deterministic <see cref="IDisposable"/> lifecycle manager
/// for all Dynamic Island services. Disposes registered services in reverse registration order on shutdown.
/// </summary>
public sealed class ServiceContainer : IDisposable
{
    private readonly Dictionary<Type, object> _services = new();
    private readonly Dictionary<Type, Func<object>> _lazyFactories = new();
    private readonly List<object> _disposables = new();
    private readonly object _lock = new();
    private bool _disposed;

    public T Register<T>(T instance) where T : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _lazyFactories.Remove(typeof(T));
            _services[typeof(T)] = instance;
            if ((instance is IDisposable || instance is IAsyncDisposable) && !_disposables.Contains(instance))
            {
                _disposables.Add(instance);
            }
            return instance;
        }
    }

    public void RegisterLazy<T>(Func<T> factory) where T : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_services.ContainsKey(typeof(T))) return;
            _lazyFactories[typeof(T)] = () => factory() ?? throw new InvalidOperationException($"Lazy factory for {typeof(T).Name} returned null.");
        }
    }

    public bool IsMaterialized<T>() where T : class
    {
        lock (_lock)
        {
            return _services.ContainsKey(typeof(T));
        }
    }

    public T Get<T>() where T : class
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_services.TryGetValue(typeof(T), out var service) && service is T typed)
            {
                return typed;
            }
            if (_lazyFactories.TryGetValue(typeof(T), out var factory))
            {
                var created = (T)factory();
                _lazyFactories.Remove(typeof(T));
                _services[typeof(T)] = created;
                if ((created is IDisposable || created is IAsyncDisposable) && !_disposables.Contains(created))
                {
                    _disposables.Add(created);
                }
                return created;
            }
        }
        throw new InvalidOperationException($"Service of type {typeof(T).Name} is not registered.");
    }

    public bool TryGet<T>(out T? service) where T : class
    {
        lock (_lock)
        {
            if (_disposed)
            {
                service = null;
                return false;
            }
            if (_services.TryGetValue(typeof(T), out var raw) && raw is T typed)
            {
                service = typed;
                return true;
            }
            if (_lazyFactories.TryGetValue(typeof(T), out var factory))
            {
                var created = (T)factory();
                _lazyFactories.Remove(typeof(T));
                _services[typeof(T)] = created;
                if ((created is IDisposable || created is IAsyncDisposable) && !_disposables.Contains(created))
                {
                    _disposables.Add(created);
                }
                service = created;
                return true;
            }
        }
        service = null;
        return false;
    }

    public int Count
    {
        get { lock (_lock) return _services.Count + _lazyFactories.Count; }
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
            _lazyFactories.Clear();
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
