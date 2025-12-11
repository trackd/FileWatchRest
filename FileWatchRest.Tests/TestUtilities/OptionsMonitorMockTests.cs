namespace FileWatchRest.Tests.TestUtilities;

public class OptionsMonitorMockTests {
    private sealed class Dummy {
        public int X { get; set; }
    }

    [Fact]
    public void OnChange_sync_listener_invoked_and_dispose_noop() {
        var monitor = new FileWatchRest.TestUtilities.OptionsMonitorMock<Dummy>();
        Dummy? observed = null;
        string? name = null;
        int called = 0;

        IDisposable disp = monitor.OnChange((t, n) => { observed = t; name = n; called++; });

        monitor.SetCurrentValue(new Dummy { X = 5 });

        Assert.Equal(1, called);
        Assert.NotNull(observed);
        Assert.Equal(5, observed!.X);
        Assert.Equal(string.Empty, name);

        disp.Dispose();

        monitor.SetCurrentValue(new Dummy { X = 6 });

        Assert.Equal(2, called);
        Assert.Equal(6, observed!.X);
    }

    [Fact]
    public void OnChange_async_listener_invoked() {
        var monitor = new FileWatchRest.TestUtilities.OptionsMonitorMock<Dummy>();
        Dummy? observed = null;
        string? name = null;
        int called = 0;

        IDisposable disp = monitor.OnChange(async (t, n) => { await Task.Yield(); observed = t; name = n; called++; });

        monitor.SetCurrentValue(new Dummy { X = 7 });

        Assert.Equal(1, called);
        Assert.NotNull(observed);
        Assert.Equal(7, observed!.X);
        Assert.Equal(string.Empty, name);

        disp.Dispose();
    }
}
