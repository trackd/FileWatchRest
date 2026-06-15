namespace FileWatchRest.Tests.Services;

public class DiagnosticsServiceTests {
    [Fact]
    public void RecordFileEvent_and_IsFilePosted_behaviour() {
        var opts = new TestOptionsMonitor();
        var svc = new DiagnosticsService(NullLogger<DiagnosticsService>.Instance, opts);

        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            string successPath = Path.Combine(dir, "a.txt");
            string failurePath = Path.Combine(dir, "b.txt");
            File.WriteAllText(successPath, "posted content");
            File.WriteAllText(failurePath, "failed content");

            svc.RecordFileEvent(successPath, true, 200);
            Assert.True(svc.IsFilePosted(successPath));

            svc.RecordFileEvent(failurePath, false, 500);
            Assert.False(svc.IsFilePosted(failurePath));

            IReadOnlyCollection<FileEventRecord> events = svc.GetRecentEvents(10);
            Assert.NotEmpty(events);
            string[] expected = [successPath, failurePath];
            Assert.All(expected, e => Assert.Contains(e, events.Select(evt => evt.Path)));
        }
        finally {
            Directory.Delete(dir, recursive: true);
        }
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
