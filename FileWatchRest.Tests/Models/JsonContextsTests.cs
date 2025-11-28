namespace FileWatchRest.Tests.Models;

public class JsonContextsTests {
    [Fact]
    public void DiagnosticStatus_CanBeInstantiatedAndPropertiesSet() {
        var diag = new DiagnosticStatus {
            ActiveWatchers = ["one", "two"],
            RestartAttempts = new Dictionary<string, int> { ["w"] = 3 },
            RecentEvents = [],
            Timestamp = DateTimeOffset.UtcNow,
            EventCount = 7,
            CircuitStates = new Dictionary<string, CircuitStateInfo>(),
            TotalEvents = 42
        };

        diag.ActiveWatchers.Should().Contain(["one", "two"]);
        diag.RestartAttempts["w"].Should().Be(3);
        diag.EventCount.Should().Be(7);
        diag.TotalEvents.Should().Be(42);
    }

    [Fact]
    public void HealthStatus_DefaultsAndSerialization() {
        var h = new HealthStatus();
        h.Status.Should().Be("healthy");
        h.Timestamp = DateTimeOffset.UtcNow;

        string json = JsonSerializer.Serialize(h);
        json.Should().Contain("healthy");
        HealthStatus round = JsonSerializer.Deserialize<HealthStatus>(json)!;
        round.Status.Should().Be("healthy");
    }

    [Fact]
    public void ErrorResponse_DefaultsAndSerialization() {
        var e = new ErrorResponse();
        e.Error.Should().BeEmpty();
        e.AvailableEndpoints.Should().NotBeNull();

        string json = JsonSerializer.Serialize(e);
        json.Should().Contain("AvailableEndpoints");
        ErrorResponse round = JsonSerializer.Deserialize<ErrorResponse>(json)!;
        round.AvailableEndpoints.Should().NotBeNull();
    }
}
