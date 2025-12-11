namespace FileWatchRest.Tests.Action;

public class ExecutableActionEndToEndTests {
    [Fact]
    public async Task ExecuteAsync_EchoOutput_IsLogged_WhenNotIgnored() {
        var fileEvent = new FileEventRecord("C:\\temp\\file_echo.txt", DateTimeOffset.UtcNow, false, null);
        var args = new List<string> { "/c", "echo", "{FilePath}" };

        var logger = new TestLogger<ExecutableAction>();
        var action = new ExecutableAction("cmd", args, logger, executionTimeoutMilliseconds: 5000, ignoreOutput: false);

        await action.ExecuteAsync(fileEvent, CancellationToken.None);

        Assert.Contains(logger.Entries, e => e.EventId.Id == 705 && e.Message.Contains(fileEvent.Path));
    }

    [Fact]
    public async Task ExecuteAsync_EchoOutput_NotLogged_WhenIgnored() {
        var fileEvent = new FileEventRecord("C:\\temp\\file_echo2.txt", DateTimeOffset.UtcNow, false, null);
        var args = new List<string> { "/c", "echo", "{FilePath}" };

        var logger = new TestLogger<ExecutableAction>();
        var action = new ExecutableAction("cmd", args, logger, executionTimeoutMilliseconds: 5000, ignoreOutput: true);

        await action.ExecuteAsync(fileEvent, CancellationToken.None);

        Assert.DoesNotContain(logger.Entries, e => e.EventId.Id == 705);
    }

    [Fact]
    public async Task ExecuteAsync_LongRunningProcess_LogsTimeoutAndIsKilled() {
        var fileEvent = new FileEventRecord("C:\\temp\\file_timeout.txt", DateTimeOffset.UtcNow, false, null);
        // ping -n 6 waits ~5 seconds on Windows
        var args = new List<string> { "/c", "ping", "-n", "6", "127.0.0.1" };

        var logger = new TestLogger<ExecutableAction>();
        var action = new ExecutableAction("cmd", args, logger, executionTimeoutMilliseconds: 5000, ignoreOutput: true);

        await action.ExecuteAsync(fileEvent, CancellationToken.None);

        Assert.Contains(logger.Entries, e => e.EventId.Id == 704);
    }
}
