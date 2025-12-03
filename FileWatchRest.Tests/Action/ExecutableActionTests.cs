namespace FileWatchRest.Tests.Action;

public class ExecutableActionTests {
    [Fact]
    public void ExecuteAsync_SubstitutesTokensAndInvokesProcessOverride() {
        var fileEvent = new FileEventRecord("C:\\temp\\file.txt", DateTimeOffset.UtcNow, false, null);
        var args = new List<string> { "/c", "echo", "-p", "{FilePath}", "--json", "{FileNotification:json}", "const" };
        var action = new ExecutableAction("cmd", args);

        ProcessStartInfo psi = action.CreateProcessStartInfo(fileEvent);

        psi.Should().NotBeNull();
        psi.FileName.Should().Be("cmd");
        psi.ArgumentList.Should().Contain("-p");
        psi.ArgumentList.Should().Contain("C:\\temp\\file.txt");
        psi.ArgumentList.Should().Contain(arg => arg.Contains("\"Path\"") && arg.Contains("file.txt"));
        return;
    }
}
