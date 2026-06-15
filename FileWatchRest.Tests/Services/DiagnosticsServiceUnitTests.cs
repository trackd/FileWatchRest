namespace FileWatchRest.Tests.Services;

public class DiagnosticsServiceUnitTests {
    [Fact]
    public void RecordFileEvent_updates_posted_status_and_counters() {
        NullLogger<DiagnosticsService> logger = NullLogger<DiagnosticsService>.Instance;
        var cfgMon = new FileWatchRest.TestUtilities.OptionsMonitorMock<ExternalConfiguration>();
        var svc = new DiagnosticsService(logger, cfgMon);

        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            string successPath = Path.Combine(dir, "f1.txt");
            string failurePath = Path.Combine(dir, "f2.txt");
            File.WriteAllText(successPath, "posted content");
            File.WriteAllText(failurePath, "failed content");

            svc.RecordFileEvent(successPath, true, 200);
            Assert.True(svc.IsFilePosted(successPath));

            svc.RecordFileEvent(failurePath, false, null);
            Assert.False(svc.IsFilePosted(failurePath));
        }
        finally {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void IsFilePosted_does_not_hash_files_without_path_match() {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            string firstPath = Path.Combine(dir, "first.txt");
            string secondPath = Path.Combine(dir, "second.txt");
            File.WriteAllText(firstPath, "same content");
            File.WriteAllText(secondPath, "same content");

            NullLogger<DiagnosticsService> logger = NullLogger<DiagnosticsService>.Instance;
            var cfgMon = new FileWatchRest.TestUtilities.OptionsMonitorMock<ExternalConfiguration>();
            var svc = new DiagnosticsService(logger, cfgMon);

            svc.RecordFileEvent(firstPath, true, 200);

            Assert.False(svc.IsFilePosted(secondPath));
        }
        finally {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void IsFilePosted_does_not_treat_changed_file_at_same_path_as_posted() {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            string path = Path.Combine(dir, "input.txt");
            File.WriteAllText(path, "original content");

            NullLogger<DiagnosticsService> logger = NullLogger<DiagnosticsService>.Instance;
            var cfgMon = new FileWatchRest.TestUtilities.OptionsMonitorMock<ExternalConfiguration>();
            var svc = new DiagnosticsService(logger, cfgMon);

            svc.RecordFileEvent(path, true, 200);
            File.WriteAllText(path, "changed content");

            Assert.False(svc.IsFilePosted(path));
        }
        finally {
            Directory.Delete(dir, recursive: true);
        }
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
