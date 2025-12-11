namespace FileWatchRest.Tests.Models;

public class CircuitStateInfoTests {
    [Fact]
    public void IsOpen_ReturnsTrue_WhenOpenUntilInFuture() {
        var c = new CircuitStateInfo { OpenUntil = DateTimeOffset.UtcNow.AddMinutes(5) };
        Assert.True(c.IsOpen);
    }

    [Fact]
    public void IsOpen_ReturnsFalse_WhenOpenUntilInPastOrNull() {
        var c = new CircuitStateInfo { OpenUntil = DateTimeOffset.UtcNow.AddMinutes(-5) };
        Assert.False(c.IsOpen);

        var c2 = new CircuitStateInfo { OpenUntil = null };
        Assert.False(c2.IsOpen);
    }
}
