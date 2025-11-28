
namespace FileWatchRest.Tests.Action;

public class ExecutableActionIgnoreOutputTests {
    [Fact]
    public void CreateProcessStartInfo_RespectsIgnoreOutputFlag() {
        var fileEvent = new FileEventRecord("C:\\temp\\file.txt", DateTimeOffset.UtcNow, false, null);
        var args = new List<string> { "/c", "echo", "{FilePath}" };

        var actionWithOutput = new ExecutableAction("cmd", args, executionTimeoutMilliseconds: 1000, ignoreOutput: false);
        ProcessStartInfo psiWith = actionWithOutput.CreateProcessStartInfo(fileEvent);
        psiWith.RedirectStandardOutput.Should().BeTrue();
        psiWith.RedirectStandardError.Should().BeTrue();

        var actionIgnore = new ExecutableAction("cmd", args, executionTimeoutMilliseconds: 1000, ignoreOutput: true);
        ProcessStartInfo psiIgnore = actionIgnore.CreateProcessStartInfo(fileEvent);
        psiIgnore.RedirectStandardOutput.Should().BeFalse();
        psiIgnore.RedirectStandardError.Should().BeFalse();
    }
}
