namespace FileWatchRest.Tests.Models;

public class CircuitStateInfoTests {
    [Fact]
    public void IsOpen_ReturnsTrue_WhenOpenUntilInFuture() {
        var c = new CircuitStateInfo { OpenUntil = DateTimeOffset.UtcNow.AddMinutes(5) };
        c.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void IsOpen_ReturnsFalse_WhenOpenUntilInPastOrNull() {
        var c = new CircuitStateInfo { OpenUntil = DateTimeOffset.UtcNow.AddMinutes(-5) };
        c.IsOpen.Should().BeFalse();

        var c2 = new CircuitStateInfo { OpenUntil = null };
        c2.IsOpen.Should().BeFalse();
    }
}
