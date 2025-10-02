namespace FileWatchRest.Tests;

public class FileWatcherManagerTests
{
    [Fact]
    public async Task SimulateWatcherError_TriggersExceededCallback()
    {
        var factory = LoggerFactory.Create(b => b.AddDebug());
        var diag = new DiagnosticsService(factory.CreateLogger<DiagnosticsService>());
        var manager = new FileWatcherManager(factory.CreateLogger<FileWatcherManager>(), diag);

        var config = new ExternalConfiguration
        {
            WatcherMaxRestartAttempts = 2,
            WatcherRestartDelayMilliseconds = 10
        };

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        manager.RegisterFolderForTest("/tmp/folder1", config, (s, e) => { }, (f, e) => { }, f => tcs.TrySetResult(true));

        // Simulate errors multiple times to exceed the max restarts
        await manager.SimulateWatcherErrorAsync("/tmp/folder1", new Exception("first"));
        await manager.SimulateWatcherErrorAsync("/tmp/folder1", new Exception("second"));
        await manager.SimulateWatcherErrorAsync("/tmp/folder1", new Exception("third"));

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        completed.Should().Be(tcs.Task);
    }
}
