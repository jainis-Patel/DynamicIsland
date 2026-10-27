namespace DynamicIsland.Core;

/// <summary>
/// Lightweight dependency container and deterministic <see cref="IDisposable"/> lifecycle manager
/// for all Dynamic Island services. Disposes registered services in reverse registration order on shutdown.
/// </summary>
public sealed class ServiceContainer : IDisposable
{
    private readonly Dictionary<Type, object> _services = new();
    private readonly List<IDisposable> _disposables = new();
    private bool _disposed;

    public T Register<T>(T instance) where T : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        _services[typeof(T)] = instance;
        if (instance is IDisposable disposable && !_disposables.Contains(disposable))
        {
            _disposables.Add(disposable);
        }
        return instance;
    }

    public T Get<T>() where T : class
    {
        if (_services.TryGetValue(typeof(T), out var service) && service is T typed)
        {
            return typed;
        }
        throw new InvalidOperationException($"Service of type {typeof(T).Name} is not registered.");
    }

    public bool TryGet<T>(out T? service) where T : class
    {
        if (_services.TryGetValue(typeof(T), out var raw) && raw is T typed)
        {
            service = typed;
            return true;
        }
        service = null;
        return false;
    }

    public int Count => _services.Count;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Dispose in reverse order of registration so dependent services clean up before shared dependencies.
        for (int i = _disposables.Count - 1; i >= 0; i--)
        {
            try
            {
                _disposables[i].Dispose();
            }
            catch (Exception ex)
            {
                App.Log($"[ServiceContainer] Error disposing {_disposables[i].GetType().Name}: {ex.Message}");
            }
        }

        _disposables.Clear();
        _services.Clear();
    }
}
