namespace FileWatchRest.Services;

/// <summary>
/// A tiny, test-friendly IOptionsMonitor implementation which holds a single value and allows manual change notifications.
/// </summary>
public class SimpleOptionsMonitor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T> : IOptionsMonitor<T>
{
    private T _value;
    // Use nullable string in delegate to match the IOptionsMonitor<T>.OnChange(Action<T,string?>) signature
    private readonly List<Action<T, string?>> _listeners = [];
    private readonly object _sync = new();

    public SimpleOptionsMonitor(T initial)
    {
        _value = initial;
    }

    public T CurrentValue => _value;

    public T Get(string? name) => _value;

    public IDisposable OnChange(Action<T, string?> listener)
    {
        lock (_sync) { _listeners.Add(listener); }
        return new DisposableAction(() => { lock (_sync) { _listeners.Remove(listener); } });
    }

    public void Raise(T newValue)
    {
        Action<T, string?>[] copy;
        lock (_sync) { _value = newValue; copy = _listeners.ToArray(); }
        foreach (var cb in copy) cb(newValue, string.Empty);
    }

    private sealed class DisposableAction : IDisposable
    {
        private readonly Action _action;
        private int _disposed;
        public DisposableAction(Action action) => _action = action;
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) _action(); }
    }
}
