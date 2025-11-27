namespace FileWatchRest.TestUtilities;

public class OptionsMonitorMock<T> : IOptionsMonitor<T> where T : class, new() {
    private readonly List<Action<T, string>> _listeners = [];
    private readonly List<Func<T, string, Task>> _asyncListeners = [];
    public T CurrentValue { get; private set; } = new T();
    public T Get(string? name) => CurrentValue;
    public IDisposable OnChange(Action<T, string> listener) {
        _listeners.Add(listener);
        return new DummyDisposable();
    }
    public IDisposable OnChange(Func<T, string, Task> asyncListener) {
        _asyncListeners.Add(asyncListener);
        return new DummyDisposable();
    }
    public void SetCurrentValue(T value) {
        CurrentValue = value;
        foreach (Action<T, string> listener in _listeners) {
            listener(CurrentValue, string.Empty);
        }
        foreach (Func<T, string, Task> asyncListener in _asyncListeners) {
            asyncListener(CurrentValue, string.Empty).GetAwaiter().GetResult();
        }
    }
    private class DummyDisposable : IDisposable { public void Dispose() { } }
}
