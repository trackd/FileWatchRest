using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Linq;
using Xunit;

namespace FileWatchRest.Tests.Services;

public class DiagnosticsServiceTests {
    [Fact]
    public void RecordFileEvent_and_IsFilePosted_behaviour()
    {
        var opts = new TestOptionsMonitor();
        var svc = new FileWatchRest.Services.DiagnosticsService(NullLogger<FileWatchRest.Services.DiagnosticsService>.Instance, opts);

        svc.RecordFileEvent("/tmp/a.txt", true, 200);
        svc.IsFilePosted("/tmp/a.txt").Should().BeTrue();

        svc.RecordFileEvent("/tmp/b.txt", false, 500);
        svc.IsFilePosted("/tmp/b.txt").Should().BeFalse();

        var events = svc.GetRecentEvents(10);
        events.Should().NotBeEmpty();
        string[] expected = new[] { "/tmp/a.txt", "/tmp/b.txt" };
        events.Select(e => e.Path).Should().Contain(expected);
    }

    [Fact]
    public void Watcher_register_and_unregister_and_restart_counts()
    {
        var opts = new TestOptionsMonitor();
        var svc = new FileWatchRest.Services.DiagnosticsService(NullLogger<FileWatchRest.Services.DiagnosticsService>.Instance, opts);

        svc.RegisterWatcher("c:\\watch");
        svc.GetActiveWatchers().Should().Contain("c:\\watch");

        svc.UnregisterWatcher("c:\\watch");
        svc.GetActiveWatchers().Should().NotContain("c:\\watch");

        int v1 = svc.IncrementRestart("c\\r");
        v1.Should().Be(1);
        int v2 = svc.IncrementRestart("c\\r");
        v2.Should().Be(2);
    }

    private sealed class TestOptionsMonitor : Microsoft.Extensions.Options.IOptionsMonitor<FileWatchRest.Configuration.ExternalConfiguration> {
        public FileWatchRest.Configuration.ExternalConfiguration CurrentValue { get; set; } = new FileWatchRest.Configuration.ExternalConfiguration();
        public FileWatchRest.Configuration.ExternalConfiguration Get(string? name) => CurrentValue;
        public IDisposable OnChange(Action<FileWatchRest.Configuration.ExternalConfiguration, string> listener) => new Dummy();
        private sealed class Dummy : IDisposable { public void Dispose() { } }
    }
}
