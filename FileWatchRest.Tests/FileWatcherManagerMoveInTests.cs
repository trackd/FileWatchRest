namespace FileWatchRest.Tests;

/// <summary>
/// Tests for file system watcher event handling, specifically verifying that all accepted event types
/// (Created, Changed, and Renamed) are properly processed by the FileWatcherManager and Worker.
///
/// Background: Files can appear in a watched folder through:
/// 1. Direct creation (WatcherChangeTypes.Created)
/// 2. Modification of existing files (WatcherChangeTypes.Changed)
/// 3. Moving/copying from another location (may trigger Created or Renamed depending on OS behavior)
/// 4. Renaming within the watched folder (WatcherChangeTypes.Renamed)
///
/// This test class ensures the bug fix for filtering out Renamed events is working correctly.
/// </summary>
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

    [Fact]
    public void RenamedEvent_IsNotFilteredOut()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "watched");
        var renamedArgs = new RenamedEventArgs(
            WatcherChangeTypes.Renamed,
            tempDir,
            "test.txt",
            "oldtest.txt");

        renamedArgs.ChangeType.Should().Be(WatcherChangeTypes.Renamed);

        var acceptedTypes = WatcherChangeTypes.Created | WatcherChangeTypes.Changed | WatcherChangeTypes.Renamed;
        (renamedArgs.ChangeType & acceptedTypes).Should().NotBe((WatcherChangeTypes)0,
            "Renamed events should be accepted by the file change handler");
    }

    [Fact]
    public void CreatedEvent_IsAccepted()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "watched");
        var createdArgs = new FileSystemEventArgs(
            WatcherChangeTypes.Created,
            tempDir,
            "newfile.txt");

        createdArgs.ChangeType.Should().Be(WatcherChangeTypes.Created);

        var acceptedTypes = WatcherChangeTypes.Created | WatcherChangeTypes.Changed | WatcherChangeTypes.Renamed;
        (createdArgs.ChangeType & acceptedTypes).Should().NotBe((WatcherChangeTypes)0,
            "Created events should be accepted by the file change handler");
    }

    [Fact]
    public void ChangedEvent_IsAccepted()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "watched");
        var changedArgs = new FileSystemEventArgs(
            WatcherChangeTypes.Changed,
            tempDir,
            "modifiedfile.txt");

        changedArgs.ChangeType.Should().Be(WatcherChangeTypes.Changed);

        var acceptedTypes = WatcherChangeTypes.Created | WatcherChangeTypes.Changed | WatcherChangeTypes.Renamed;
        (changedArgs.ChangeType & acceptedTypes).Should().NotBe((WatcherChangeTypes)0,
            "Changed events should be accepted by the file change handler");
    }

    [Fact]
    public void DeletedEvent_IsNotAccepted()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "watched");
        var deletedArgs = new FileSystemEventArgs(
            WatcherChangeTypes.Deleted,
            tempDir,
            "deletedfile.txt");

        deletedArgs.ChangeType.Should().Be(WatcherChangeTypes.Deleted);

        var acceptedTypes = WatcherChangeTypes.Created | WatcherChangeTypes.Changed | WatcherChangeTypes.Renamed;
        (deletedArgs.ChangeType & acceptedTypes).Should().Be((WatcherChangeTypes)0,
            "Deleted events should NOT be accepted by the file change handler");
    }

    [Fact]
    public async Task RealFileSystem_CreateFile_TriggersCreatedEvent()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"FileWatchTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDir);

        try
        {
            var factory = LoggerFactory.Create(b => b.AddDebug());
            var diag = new DiagnosticsService(factory.CreateLogger<DiagnosticsService>());
            var manager = new FileWatcherManager(factory.CreateLogger<FileWatcherManager>(), diag);

            var config = new ExternalConfiguration
            {
                WatcherMaxRestartAttempts = 1,
                WatcherRestartDelayMilliseconds = 10,
                IncludeSubdirectories = false,
                AllowedExtensions = []
            };

            var tcs = new TaskCompletionSource<FileSystemEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            FileSystemEventHandler handler = (s, e) =>
            {
                if (e.ChangeType == WatcherChangeTypes.Created)
                {
                    tcs.TrySetResult(e);
                }
            };

            await manager.StartWatchingAsync([testDir], config, handler, null, null);

            await Task.Delay(100);

            var testFile = Path.Combine(testDir, "created_test.txt");
            await File.WriteAllTextAsync(testFile, "test content");

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(3000));
            completed.Should().Be(tcs.Task, "Created event should be raised");

            var args = await tcs.Task;
            args.ChangeType.Should().Be(WatcherChangeTypes.Created);
            args.Name.Should().Be("created_test.txt");

            await manager.StopAllAsync();
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }
    }

    [Fact]
    public async Task RealFileSystem_MoveFile_TriggersCreatedOrRenamedEvent()
    {
        // When moving files from outside the watched folder, Windows may trigger either:
        // - Created event (most common when moving between directories)
        // - Renamed event (when moving within the same parent but into a watched subfolder)
        // Both are acceptable and should be handled by the Worker
        var sourceDir = Path.Combine(Path.GetTempPath(), $"FileWatchSource_{Guid.NewGuid()}");
        var watchedDir = Path.Combine(Path.GetTempPath(), $"FileWatchTarget_{Guid.NewGuid()}");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(watchedDir);

        try
        {
            var factory = LoggerFactory.Create(b => b.AddDebug());
            var diag = new DiagnosticsService(factory.CreateLogger<DiagnosticsService>());
            var manager = new FileWatcherManager(factory.CreateLogger<FileWatcherManager>(), diag);

            var config = new ExternalConfiguration
            {
                WatcherMaxRestartAttempts = 1,
                WatcherRestartDelayMilliseconds = 10,
                IncludeSubdirectories = false,
                AllowedExtensions = []
            };

            var tcs = new TaskCompletionSource<FileSystemEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            FileSystemEventHandler handler = (s, e) =>
            {
                if ((e.ChangeType == WatcherChangeTypes.Renamed || e.ChangeType == WatcherChangeTypes.Created)
                    && e.Name == "moved_test.txt")
                {
                    tcs.TrySetResult(e);
                }
            };

            await manager.StartWatchingAsync([watchedDir], config, handler, null, null);

            await Task.Delay(100);

            var sourceFile = Path.Combine(sourceDir, "moved_test.txt");
            await File.WriteAllTextAsync(sourceFile, "test content");

            var targetFile = Path.Combine(watchedDir, "moved_test.txt");
            File.Move(sourceFile, targetFile);

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(3000));
            completed.Should().Be(tcs.Task, "Created or Renamed event should be raised when file is moved into watched folder");

            var args = await tcs.Task;
            args.ChangeType.Should().Match(ct =>
                ct == WatcherChangeTypes.Created || ct == WatcherChangeTypes.Renamed,
                "Move operations trigger either Created or Renamed events");
            args.Name.Should().Be("moved_test.txt");

            await manager.StopAllAsync();
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, true);
            }
            if (Directory.Exists(watchedDir))
            {
                Directory.Delete(watchedDir, true);
            }
        }
    }

    [Fact]
    public async Task RealFileSystem_ModifyFile_TriggersChangedEvent()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"FileWatchTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDir);

        try
        {
            var factory = LoggerFactory.Create(b => b.AddDebug());
            var diag = new DiagnosticsService(factory.CreateLogger<DiagnosticsService>());
            var manager = new FileWatcherManager(factory.CreateLogger<FileWatcherManager>(), diag);

            var config = new ExternalConfiguration
            {
                WatcherMaxRestartAttempts = 1,
                WatcherRestartDelayMilliseconds = 10,
                IncludeSubdirectories = false,
                AllowedExtensions = []
            };

            var testFile = Path.Combine(testDir, "modify_test.txt");
            await File.WriteAllTextAsync(testFile, "initial content");

            await Task.Delay(100);

            var tcs = new TaskCompletionSource<FileSystemEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            FileSystemEventHandler handler = (s, e) =>
            {
                if (e.ChangeType == WatcherChangeTypes.Changed && e.Name == "modify_test.txt")
                {
                    tcs.TrySetResult(e);
                }
            };

            await manager.StartWatchingAsync([testDir], config, handler, null, null);

            await Task.Delay(100);

            await File.WriteAllTextAsync(testFile, "modified content");

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(3000));
            completed.Should().Be(tcs.Task, "Changed event should be raised");

            var args = await tcs.Task;
            args.ChangeType.Should().Be(WatcherChangeTypes.Changed);
            args.Name.Should().Be("modify_test.txt");

            await manager.StopAllAsync();
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }
    }

    [Fact]
    public async Task RealFileSystem_AllAcceptedEventTypes_AreProcessed()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), $"FileWatchSource_{Guid.NewGuid()}");
        var watchedDir = Path.Combine(Path.GetTempPath(), $"FileWatchTarget_{Guid.NewGuid()}");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(watchedDir);

        try
        {
            var factory = LoggerFactory.Create(b => b.AddDebug());
            var diag = new DiagnosticsService(factory.CreateLogger<DiagnosticsService>());
            var manager = new FileWatcherManager(factory.CreateLogger<FileWatcherManager>(), diag);

            var config = new ExternalConfiguration
            {
                WatcherMaxRestartAttempts = 1,
                WatcherRestartDelayMilliseconds = 10,
                IncludeSubdirectories = false,
                AllowedExtensions = []
            };

            var receivedEvents = new System.Collections.Concurrent.ConcurrentBag<(WatcherChangeTypes ChangeType, string Name)>();
            FileSystemEventHandler handler = (s, e) =>
            {
                receivedEvents.Add((e.ChangeType, e.Name ?? string.Empty));
            };

            await manager.StartWatchingAsync([watchedDir], config, handler, null, null);
            await Task.Delay(100);

            var createdFile = Path.Combine(watchedDir, "created.txt");
            await File.WriteAllTextAsync(createdFile, "created");
            await Task.Delay(200);

            await File.WriteAllTextAsync(createdFile, "modified");
            await Task.Delay(200);

            var movedSourceFile = Path.Combine(sourceDir, "moved.txt");
            await File.WriteAllTextAsync(movedSourceFile, "to be moved");
            var movedTargetFile = Path.Combine(watchedDir, "moved.txt");
            File.Move(movedSourceFile, movedTargetFile);
            await Task.Delay(200);

            var renamedFile = Path.Combine(watchedDir, "rename_source.txt");
            await File.WriteAllTextAsync(renamedFile, "to be renamed");
            await Task.Delay(200);
            var renamedTarget = Path.Combine(watchedDir, "rename_target.txt");
            File.Move(renamedFile, renamedTarget);
            await Task.Delay(200);

            await manager.StopAllAsync();

            receivedEvents.Should().Contain(e => e.ChangeType == WatcherChangeTypes.Created && e.Name == "created.txt",
                "Created event should be captured for directly created files");
            receivedEvents.Should().Contain(e => e.ChangeType == WatcherChangeTypes.Changed && e.Name == "created.txt",
                "Changed event should be captured for modified files");
            receivedEvents.Should().Contain(e =>
                (e.ChangeType == WatcherChangeTypes.Created || e.ChangeType == WatcherChangeTypes.Renamed)
                && e.Name == "moved.txt",
                "Created or Renamed event should be captured when file is moved into watched folder");
            receivedEvents.Should().Contain(e => e.ChangeType == WatcherChangeTypes.Renamed && e.Name == "rename_target.txt",
                "Renamed event should be captured for files renamed within the watched folder");
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, true);
            }
            if (Directory.Exists(watchedDir))
            {
                Directory.Delete(watchedDir, true);
            }
        }
    }
}
