namespace FileWatchRest.Tests;

public class FileWatcherManagerMoveInTests
{
    [Fact]
    public async Task MoveFileIntoWatchedFolder_TriggersOnChangedAsRenamed()
    {
        var factory = LoggerFactory.Create(b => b.AddDebug());
        var diag = new DiagnosticsService(factory.CreateLogger<DiagnosticsService>());
        var manager = new FileWatcherManager(factory.CreateLogger<FileWatcherManager>(), diag);

        var config = new ExternalConfiguration
        {
            WatcherMaxRestartAttempts = 1,
            WatcherRestartDelayMilliseconds = 10
        };

        var tcs = new TaskCompletionSource<RenamedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        FileSystemEventHandler handler = (s, e) =>
        {
            if (e is RenamedEventArgs re) tcs.TrySetResult(re);
        };

        manager.RegisterFolderForTest("C:\\watched", config, handler, null, null);

        var oldPath = Path.Combine(Path.GetTempPath(), "tempfile.tmp");
        var newPath = Path.Combine("C:\\watched", "movedfile.tmp");

        await manager.SimulateRenamedAsync("C:\\watched", oldPath, newPath);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        completed.Should().Be(tcs.Task);
        var args = await tcs.Task;
    args.FullPath.Should().Be(newPath);
    args.OldName.Should().Be(Path.GetFileName(oldPath));
    }
}
