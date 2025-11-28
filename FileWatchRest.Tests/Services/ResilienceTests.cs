using FluentAssertions;
using FileWatchRest.Services;
using System;
using Xunit;

namespace FileWatchRest.Tests.Services;

public class ResilienceTests {
    [Fact]
    public void ResilienceResult_properties_work_as_record() {
        var ex = new InvalidOperationException("boom");
        var r = new ResilienceResult(false, 3, 500, ex, 123, false);

        r.Success.Should().BeFalse();
        r.Attempts.Should().Be(3);
        r.LastStatusCode.Should().Be(500);
        r.LastException.Should().BeSameAs(ex);
        r.TotalElapsedMs.Should().Be(123);
        r.ShortCircuited.Should().BeFalse();
    }
}
