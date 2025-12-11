namespace FileWatchRest.Tests.Services;

public class DiagnosticsServiceUnitTests {
    [Fact]
    public void RecordFileEvent_updates_posted_status_and_counters() {
        NullLogger<DiagnosticsService> logger = NullLogger<DiagnosticsService>.Instance;
        var cfgMon = new FileWatchRest.TestUtilities.OptionsMonitorMock<ExternalConfiguration>();
        var svc = new DiagnosticsService(logger, cfgMon);

        svc.RecordFileEvent("/tmp/f1", true, 200);
        Assert.True(svc.IsFilePosted("/tmp/f1"));

        svc.RecordFileEvent("/tmp/f2", false, null);
        Assert.False(svc.IsFilePosted("/tmp/f2"));
    }

    [Fact]
    public void Register_and_unregister_watcher_and_restart_counts() {
        NullLogger<DiagnosticsService> logger = NullLogger<DiagnosticsService>.Instance;
        var cfgMon = new FileWatchRest.TestUtilities.OptionsMonitorMock<ExternalConfiguration>();
        var svc = new DiagnosticsService(logger, cfgMon);

        svc.RegisterWatcher("C:\\foo");
        Assert.Contains("C:\\foo", svc.GetActiveWatchers());

        svc.UnregisterWatcher("C:\\foo");
        Assert.DoesNotContain("C:\\foo", svc.GetActiveWatchers());

        Assert.Equal(1, svc.IncrementRestart("C:\\a"));
        Assert.Equal(2, svc.IncrementRestart("C:\\a"));
        svc.ResetRestart("C:\\a");
        Assert.DoesNotContain("C:\\a", svc.GetRestartAttemptsSnapshot().Keys);
    }

    [Fact]
    public void GetRecentEvents_returns_events_in_reverse_order() {
        NullLogger<DiagnosticsService> logger = NullLogger<DiagnosticsService>.Instance;
        var cfgMon = new FileWatchRest.TestUtilities.OptionsMonitorMock<ExternalConfiguration>();
        var svc = new DiagnosticsService(logger, cfgMon);

        svc.RecordFileEvent("p1", true, 200);
        svc.RecordFileEvent("p2", false, 500);
        var events = svc.GetRecentEvents(10).ToList();
        Assert.True(events.Count >= 2);
        Assert.Equal("p2", events[0].Path);
        Assert.Equal("p1", events[1].Path);
    }
}
