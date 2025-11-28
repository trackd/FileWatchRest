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

        called.Should().Be(1);
        observed.Should().NotBeNull();
        observed!.X.Should().Be(5);
        name.Should().Be(string.Empty);

        disp.Dispose();

        monitor.SetCurrentValue(new Dummy { X = 6 });

        called.Should().Be(2);
        observed!.X.Should().Be(6);
    }

    [Fact]
    public void OnChange_async_listener_invoked() {
        var monitor = new FileWatchRest.TestUtilities.OptionsMonitorMock<Dummy>();
        Dummy? observed = null;
        string? name = null;
        int called = 0;

        IDisposable disp = monitor.OnChange(async (t, n) => { await Task.Yield(); observed = t; name = n; called++; });

        monitor.SetCurrentValue(new Dummy { X = 7 });

        called.Should().Be(1);
        observed.Should().NotBeNull();
        observed!.X.Should().Be(7);
        name.Should().Be(string.Empty);

        disp.Dispose();
    }
}
