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

        Assert.Contains("one", diag.ActiveWatchers);
        Assert.Contains("two", diag.ActiveWatchers);
        Assert.Equal(3, diag.RestartAttempts["w"]);
        Assert.Equal(7, diag.EventCount);
        Assert.Equal(42, diag.TotalEvents);
    }

    [Fact]
    public void HealthStatus_DefaultsAndSerialization() {
        var h = new HealthStatus();
        Assert.Equal("healthy", h.Status);
        h.Timestamp = DateTimeOffset.UtcNow;

        string json = JsonSerializer.Serialize(h);
        Assert.Contains("healthy", json);
        HealthStatus round = JsonSerializer.Deserialize<HealthStatus>(json)!;
        Assert.Equal("healthy", round.Status);
    }

    [Fact]
    public void ErrorResponse_DefaultsAndSerialization() {
        var e = new ErrorResponse();
        Assert.Empty(e.Error);
        Assert.NotNull(e.AvailableEndpoints);

        string json = JsonSerializer.Serialize(e);
        Assert.Contains("AvailableEndpoints", json);
        ErrorResponse round = JsonSerializer.Deserialize<ErrorResponse>(json)!;
        Assert.NotNull(round.AvailableEndpoints);
    }
}
