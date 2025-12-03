
namespace FileWatchRest.Services;

/// <summary>
/// <para>Test helper for IOptionsMonitor - provides a simple implementation for unit testing.</para>
/// <para>
/// Use this in tests to:
/// - Inject test configuration values without complex DI setup
/// - Manually trigger configuration changes via Raise()
/// - Verify that components properly respond to configuration updates
/// </para>
/// <para>Production code uses ExternalConfigurationOptionsMonitor instead.</para>
/// </summary>
/// <typeparam name="T"></typeparam>
/// <param name="initial"></param>
public class SimpleOptionsMonitor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(T initial) : IOptionsMonitor<T> {
    /// <summary>
    /// Use nullable string in delegate to match the IOptionsMonitor<T>.OnChange(Action<T,string?>) signature
    /// </summary>
    private readonly List<Action<T, string?>> _listeners = [];
    private readonly Lock _sync = new();

    public T CurrentValue { get; private set; } = initial;

    public T Get(string? name) => CurrentValue;

    public IDisposable OnChange(Action<T, string?> listener) {
        lock (_sync) { _listeners.Add(listener); }
        return new DisposableAction(() => { lock (_sync) { _listeners.Remove(listener); } });
    }

    public void Raise(T newValue) {
        Action<T, string?>[] copy;
        lock (_sync) { CurrentValue = newValue; copy = [.. _listeners]; }
        foreach (Action<T, string?> cb in copy) {
            cb(newValue, string.Empty);
        }
    }

    private sealed class DisposableAction(Action action) : IDisposable {
        private readonly Action _action = action;
        private int _disposed;

        public void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) {
                _action();
            }
        }
    }
}
