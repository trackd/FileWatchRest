namespace FileWatchRest.Tests.TestUtilities;

public class OptionsMonitorMock<T> : IOptionsMonitor<T> where T : class, new() {
    private readonly List<Action<T, string>> _listeners = [];
    public T CurrentValue { get; private set; } = new();
    public T Get(string? name) => CurrentValue;
    public IDisposable OnChange(Action<T, string> listener) {
        _listeners.Add(listener);
        return new DummyDisposable();
    }
    public void SetCurrentValue(T value) {
        CurrentValue = value;
        foreach (Action<T, string> listener in _listeners) {
            listener(CurrentValue, string.Empty);
        }
    }
    private sealed class DummyDisposable : IDisposable { public void Dispose() { } }
}
