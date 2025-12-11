namespace FileWatchRest.Tests.Services;

public class DiagnosticsServiceTests {
    [Fact]
    public void RecordFileEvent_and_IsFilePosted_behaviour() {
        var opts = new TestOptionsMonitor();
        var svc = new DiagnosticsService(NullLogger<DiagnosticsService>.Instance, opts);

        svc.RecordFileEvent("/tmp/a.txt", true, 200);
        Assert.True(svc.IsFilePosted("/tmp/a.txt"));

        svc.RecordFileEvent("/tmp/b.txt", false, 500);
        Assert.False(svc.IsFilePosted("/tmp/b.txt"));

        IReadOnlyCollection<FileEventRecord> events = svc.GetRecentEvents(10);
        Assert.NotEmpty(events);
        string[] expected = ["/tmp/a.txt", "/tmp/b.txt"];
        Assert.All(expected, e => Assert.Contains(e, events.Select(evt => evt.Path)));
    }

    [Fact]
    public void Watcher_register_and_unregister_and_restart_counts() {
        var opts = new TestOptionsMonitor();
        var svc = new DiagnosticsService(NullLogger<DiagnosticsService>.Instance, opts);

        svc.RegisterWatcher("c:\\watch");
        Assert.Contains("c:\\watch", svc.GetActiveWatchers());

        svc.UnregisterWatcher("c:\\watch");
        Assert.DoesNotContain("c:\\watch", svc.GetActiveWatchers());

        int v1 = svc.IncrementRestart("c\\r");
        Assert.Equal(1, v1);
        int v2 = svc.IncrementRestart("c\\r");
        Assert.Equal(2, v2);
    }

    private sealed class TestOptionsMonitor : IOptionsMonitor<ExternalConfiguration> {
        public ExternalConfiguration CurrentValue { get; set; } = new ExternalConfiguration();
        public ExternalConfiguration Get(string? name) => CurrentValue;
        public IDisposable OnChange(Action<ExternalConfiguration, string> listener) => new Dummy();
        private sealed class Dummy : IDisposable { public void Dispose() { } }
    }
}
