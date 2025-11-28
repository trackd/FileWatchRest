using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using FileWatchRest.Services;
using FileWatchRest.Configuration;

namespace FileWatchRest.Tests.Services
{
    public class DiagnosticsServiceUnitTests
    {
        [Fact]
        public void RecordFileEvent_updates_posted_status_and_counters()
        {
            var logger = NullLogger<DiagnosticsService>.Instance;
            var cfgMon = new FileWatchRest.TestUtilities.OptionsMonitorMock<ExternalConfiguration>();
            var svc = new DiagnosticsService(logger, cfgMon);

            svc.RecordFileEvent("/tmp/f1", true, 200);
            svc.IsFilePosted("/tmp/f1").Should().BeTrue();

            svc.RecordFileEvent("/tmp/f2", false, null);
            svc.IsFilePosted("/tmp/f2").Should().BeFalse();
        }

        [Fact]
        public void Register_and_unregister_watcher_and_restart_counts()
        {
            var logger = NullLogger<DiagnosticsService>.Instance;
            var cfgMon = new FileWatchRest.TestUtilities.OptionsMonitorMock<ExternalConfiguration>();
            var svc = new DiagnosticsService(logger, cfgMon);

            svc.RegisterWatcher("C:\\foo");
            svc.GetActiveWatchers().Should().Contain("C:\\foo");

            svc.UnregisterWatcher("C:\\foo");
            svc.GetActiveWatchers().Should().NotContain("C:\\foo");

            svc.IncrementRestart("C:\\a").Should().Be(1);
            svc.IncrementRestart("C:\\a").Should().Be(2);
            svc.ResetRestart("C:\\a");
            svc.GetRestartAttemptsSnapshot().Should().NotContainKey("C:\\a");
        }

        [Fact]
        public void GetRecentEvents_returns_events_in_reverse_order()
        {
            var logger = NullLogger<DiagnosticsService>.Instance;
            var cfgMon = new FileWatchRest.TestUtilities.OptionsMonitorMock<ExternalConfiguration>();
            var svc = new DiagnosticsService(logger, cfgMon);

            svc.RecordFileEvent("p1", true, 200);
            svc.RecordFileEvent("p2", false, 500);
            var events = svc.GetRecentEvents(10).ToList();
            events.Count.Should().BeGreaterOrEqualTo(2);
            events[0].Path.Should().Be("p2");
            events[1].Path.Should().Be("p1");
        }
    }
}
