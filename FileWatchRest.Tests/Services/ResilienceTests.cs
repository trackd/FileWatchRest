namespace FileWatchRest.Tests.Services;

public class ResilienceTests {
    [Fact]
    public void ResilienceResult_properties_work_as_record() {
        var ex = new InvalidOperationException("boom");
        var r = new ResilienceResult(false, 3, 500, ex, 123, false);

        Assert.False(r.Success);
        Assert.Equal(3, r.Attempts);
        Assert.Equal(500, r.LastStatusCode);
        Assert.Same(ex, r.LastException);
        Assert.Equal(123, r.TotalElapsedMs);
        Assert.False(r.ShortCircuited);
    }
}
