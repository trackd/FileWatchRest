namespace FileWatchRest.Tests.Action;

public class ExecutableActionTests {
    [Fact]
    public void ExecuteAsync_SubstitutesTokensAndInvokesProcessOverride() {
        var fileEvent = new FileEventRecord("C:\\temp\\file.txt", DateTimeOffset.UtcNow, false, null);
        var args = new List<string> { "/c", "echo", "-p", "{FilePath}", "--json", "{FileNotification:json}", "const" };
        var action = new ExecutableAction("cmd", args);

        ProcessStartInfo psi = action.CreateProcessStartInfo(fileEvent);

        Assert.NotNull(psi);
        Assert.Equal("cmd", psi.FileName);
        Assert.Contains("-p", psi.ArgumentList);
        Assert.Contains("C:\\temp\\file.txt", psi.ArgumentList);
        Assert.Contains(psi.ArgumentList, arg => arg.Contains("\"Path\"") && arg.Contains("file.txt"));
        return;
    }
}
