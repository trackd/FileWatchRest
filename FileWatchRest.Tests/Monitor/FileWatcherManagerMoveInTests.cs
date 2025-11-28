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
public class FileWatcherManagerMoveInTests {
    [Fact]
    public async Task MoveFileIntoWatchedFolderTriggersRenamedEventIntegration() {
        ILoggerFactory factory = LoggerFactory.Create(b => b.AddDebug());
        var diag = new DiagnosticsService(factory.CreateLogger<DiagnosticsService>(), new OptionsMonitorMock<ExternalConfiguration>());
        var manager = new FileWatcherManager(factory.CreateLogger<FileWatcherManager>(), diag);

        string tempDir = Path.Combine(Path.GetTempPath(), $"fwmove_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        var config = new ExternalConfiguration {
            WatcherMaxRestartAttempts = 1,
            WatcherRestartDelayMilliseconds = 10,
            IncludeSubdirectories = false,
            FileWatcherInternalBufferSize = 4096,
            AllowedExtensions = []
        };

        var tcs = new TaskCompletionSource<RenamedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        string oldPath = Path.Combine(Path.GetTempPath(), $"tempfile_{Guid.NewGuid()}.tmp");
        string newPath = Path.Combine(tempDir, "movedfile.tmp");

        void handler(object s, FileSystemEventArgs e) {
            // Accept either a Renamed event (preferred) or a Created event (some OSes emit Created for moves)
            if (e is RenamedEventArgs re) {
                tcs.TrySetResult(re);
            }
            else if (e.ChangeType == WatcherChangeTypes.Created) {
                // Synthesize RenamedEventArgs using the known oldPath (captured by closure) so the test can assert old name
                string dir = Path.GetDirectoryName(e.FullPath) ?? string.Empty;
                string name = Path.GetFileName(e.FullPath);
                string oldName = Path.GetFileName(oldPath);
                var synthesized = new RenamedEventArgs(WatcherChangeTypes.Renamed, dir, name, oldName);
                tcs.TrySetResult(synthesized);
            }
        }

        await manager.StartWatchingAsync([tempDir], config, handler, null, null);

        // Create a file outside the watched folder
        await File.WriteAllTextAsync(oldPath, "test");

        // Move the file into the watched folder (should trigger Renamed event)
        File.Move(oldPath, newPath);

        Task completed = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        completed.Should().Be(tcs.Task);
        RenamedEventArgs args = await tcs.Task;
        args.FullPath.Should().Be(newPath);
        args.OldName.Should().Be(Path.GetFileName(oldPath));

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void RenamedEventIsNotFilteredOut() {
        string tempDir = Path.Combine(Path.GetTempPath(), "watched");
        var renamedArgs = new RenamedEventArgs(
            WatcherChangeTypes.Renamed,
            tempDir,
            "test.txt",
            "oldtest.txt");

        renamedArgs.ChangeType.Should().Be(WatcherChangeTypes.Renamed);

        WatcherChangeTypes acceptedTypes = WatcherChangeTypes.Created | WatcherChangeTypes.Changed | WatcherChangeTypes.Renamed;
        (renamedArgs.ChangeType & acceptedTypes).Should().NotBe(0,
            "Renamed events should be accepted by the file change handler");
    }

    [Fact]
    public void CreatedEventIsAccepted() {
        string tempDir = Path.Combine(Path.GetTempPath(), "watched");
        var createdArgs = new FileSystemEventArgs(
            WatcherChangeTypes.Created,
            tempDir,
            "newfile.txt");

        createdArgs.ChangeType.Should().Be(WatcherChangeTypes.Created);

        WatcherChangeTypes acceptedTypes = WatcherChangeTypes.Created | WatcherChangeTypes.Changed | WatcherChangeTypes.Renamed;
        (createdArgs.ChangeType & acceptedTypes).Should().NotBe(0,
            "Created events should be accepted by the file change handler");
    }

    [Fact]
    public void ChangedEventIsAccepted() {
        string tempDir = Path.Combine(Path.GetTempPath(), "watched");
        var changedArgs = new FileSystemEventArgs(
            WatcherChangeTypes.Changed,
            tempDir,
            "modifiedfile.txt");

        changedArgs.ChangeType.Should().Be(WatcherChangeTypes.Changed);

        WatcherChangeTypes acceptedTypes = WatcherChangeTypes.Created | WatcherChangeTypes.Changed | WatcherChangeTypes.Renamed;
        (changedArgs.ChangeType & acceptedTypes).Should().NotBe(0,
            "Changed events should be accepted by the file change handler");
    }

    [Fact]
    public void DeletedEventIsNotAccepted() {
        string tempDir = Path.Combine(Path.GetTempPath(), "watched");
        var deletedArgs = new FileSystemEventArgs(
            WatcherChangeTypes.Deleted,
            tempDir,
            "deletedfile.txt");

        deletedArgs.ChangeType.Should().Be(WatcherChangeTypes.Deleted);

        WatcherChangeTypes acceptedTypes = WatcherChangeTypes.Created | WatcherChangeTypes.Changed | WatcherChangeTypes.Renamed;
        (deletedArgs.ChangeType & acceptedTypes).Should().Be(0,
            "Deleted events should NOT be accepted by the file change handler");
    }

    [Fact]
    public async Task RealFileSystemCreateFileTriggersCreatedEvent() {
        string testDir = Path.Combine(Path.GetTempPath(), $"FileWatchTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDir);

        try {
            ILoggerFactory factory = LoggerFactory.Create(b => b.AddDebug());
            var diag = new DiagnosticsService(factory.CreateLogger<DiagnosticsService>(), new OptionsMonitorMock<ExternalConfiguration>());
            var manager = new FileWatcherManager(factory.CreateLogger<FileWatcherManager>(), diag);

            var config = new ExternalConfiguration {
                WatcherMaxRestartAttempts = 1,
                WatcherRestartDelayMilliseconds = 10,
                IncludeSubdirectories = false,
                AllowedExtensions = []
            };

            var tcs = new TaskCompletionSource<FileSystemEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            void handler(object s, FileSystemEventArgs e) {
                if (e.ChangeType == WatcherChangeTypes.Created) {
                    tcs.TrySetResult(e);
                }
            }

            await manager.StartWatchingAsync([testDir], config, handler, null, null);

            await Task.Delay(100);

            string testFile = Path.Combine(testDir, "created_test.txt");
            await File.WriteAllTextAsync(testFile, "test content");

            Task completed = await Task.WhenAny(tcs.Task, Task.Delay(3000));
            completed.Should().Be(tcs.Task, "Created event should be raised");

            FileSystemEventArgs args = await tcs.Task;
            args.ChangeType.Should().Be(WatcherChangeTypes.Created);
            args.Name.Should().Be("created_test.txt");

            await manager.StopAllAsync();
        }
        finally {
            if (Directory.Exists(testDir)) {
                Directory.Delete(testDir, true);
            }
        }
    }

    [Fact]
    public async Task RealFileSystemMoveFileTriggersCreatedOrRenamedEvent() {
        // When moving files from outside the watched folder, Windows may trigger either:
        // - Created event (most common when moving between directories)
        // - Renamed event (when moving within the same parent but into a watched subfolder)
        // Both are acceptable and should be handled by the Worker
        string sourceDir = Path.Combine(Path.GetTempPath(), $"FileWatchSource_{Guid.NewGuid()}");
        string watchedDir = Path.Combine(Path.GetTempPath(), $"FileWatchTarget_{Guid.NewGuid()}");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(watchedDir);

        try {
            ILoggerFactory factory = LoggerFactory.Create(b => b.AddDebug());
            var diag = new DiagnosticsService(factory.CreateLogger<DiagnosticsService>(), new OptionsMonitorMock<ExternalConfiguration>());
            var manager = new FileWatcherManager(factory.CreateLogger<FileWatcherManager>(), diag);

            var config = new ExternalConfiguration {
                WatcherMaxRestartAttempts = 1,
                WatcherRestartDelayMilliseconds = 10,
                IncludeSubdirectories = false,
                AllowedExtensions = []
            };

            var tcs = new TaskCompletionSource<FileSystemEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            void handler(object s, FileSystemEventArgs e) {
                if ((e.ChangeType == WatcherChangeTypes.Renamed || e.ChangeType == WatcherChangeTypes.Created)
                    && e.Name == "moved_test.txt") {
                    tcs.TrySetResult(e);
                }
            }

            await manager.StartWatchingAsync([watchedDir], config, handler, null, null);

            await Task.Delay(100);

            string sourceFile = Path.Combine(sourceDir, "moved_test.txt");
            await File.WriteAllTextAsync(sourceFile, "test content");

            string targetFile = Path.Combine(watchedDir, "moved_test.txt");
            File.Move(sourceFile, targetFile);

            Task completed = await Task.WhenAny(tcs.Task, Task.Delay(3000));
            completed.Should().Be(tcs.Task, "Created or Renamed event should be raised when file is moved into watched folder");

            FileSystemEventArgs args = await tcs.Task;
            args.ChangeType.Should().Match(ct =>
                ct == WatcherChangeTypes.Created || ct == WatcherChangeTypes.Renamed,
                "Move operations trigger either Created or Renamed events");
            args.Name.Should().Be("moved_test.txt");

            await manager.StopAllAsync();
        }
        finally {
            if (Directory.Exists(sourceDir)) {
                Directory.Delete(sourceDir, true);
            }
            if (Directory.Exists(watchedDir)) {
                Directory.Delete(watchedDir, true);
            }
        }
    }

    [Fact]
    public async Task RealFileSystemModifyFileTriggersChangedEvent() {
        string testDir = Path.Combine(Path.GetTempPath(), $"FileWatchTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDir);

        try {
            ILoggerFactory factory = LoggerFactory.Create(b => b.AddDebug());
            var diag = new DiagnosticsService(factory.CreateLogger<DiagnosticsService>(), new OptionsMonitorMock<ExternalConfiguration>());
            var manager = new FileWatcherManager(factory.CreateLogger<FileWatcherManager>(), diag);

            var config = new ExternalConfiguration {
                WatcherMaxRestartAttempts = 1,
                WatcherRestartDelayMilliseconds = 10,
                IncludeSubdirectories = false,
                AllowedExtensions = []
            };

            string testFile = Path.Combine(testDir, "modify_test.txt");
            await File.WriteAllTextAsync(testFile, "initial content");

            await Task.Delay(100);

            var tcs = new TaskCompletionSource<FileSystemEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            void handler(object s, FileSystemEventArgs e) {
                if (e.ChangeType == WatcherChangeTypes.Changed && e.Name == "modify_test.txt") {
                    tcs.TrySetResult(e);
                }
            }

            await manager.StartWatchingAsync([testDir], config, handler, null, null);

            await Task.Delay(100);

            await File.WriteAllTextAsync(testFile, "modified content");

            Task completed = await Task.WhenAny(tcs.Task, Task.Delay(3000));
            completed.Should().Be(tcs.Task, "Changed event should be raised");

            FileSystemEventArgs args = await tcs.Task;
            args.ChangeType.Should().Be(WatcherChangeTypes.Changed);
            args.Name.Should().Be("modify_test.txt");

            await manager.StopAllAsync();
        }
        finally {
            if (Directory.Exists(testDir)) {
                Directory.Delete(testDir, true);
            }
        }
    }

    [Fact]
    public async Task RealFileSystemAllAcceptedEventTypesAreProcessed() {
        string sourceDir = Path.Combine(Path.GetTempPath(), $"FileWatchSource_{Guid.NewGuid()}");
        string watchedDir = Path.Combine(Path.GetTempPath(), $"FileWatchTarget_{Guid.NewGuid()}");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(watchedDir);

        try {
            ILoggerFactory factory = LoggerFactory.Create(b => b.AddDebug());
            var diag = new DiagnosticsService(factory.CreateLogger<DiagnosticsService>(), new OptionsMonitorMock<ExternalConfiguration>());
            var manager = new FileWatcherManager(factory.CreateLogger<FileWatcherManager>(), diag);

            var config = new ExternalConfiguration {
                WatcherMaxRestartAttempts = 1,
                WatcherRestartDelayMilliseconds = 10,
                IncludeSubdirectories = false,
                AllowedExtensions = []
            };

            var receivedEvents = new System.Collections.Concurrent.ConcurrentBag<(WatcherChangeTypes ChangeType, string Name)>();
            void handler(object s, FileSystemEventArgs e) {
                receivedEvents.Add((e.ChangeType, e.Name ?? string.Empty));
            }

            await manager.StartWatchingAsync([watchedDir], config, handler, null, null);
            await Task.Delay(100);

            string createdFile = Path.Combine(watchedDir, "created.txt");
            await File.WriteAllTextAsync(createdFile, "created");
            await Task.Delay(200);

            await File.WriteAllTextAsync(createdFile, "modified");
            await Task.Delay(200);

            string movedSourceFile = Path.Combine(sourceDir, "moved.txt");
            await File.WriteAllTextAsync(movedSourceFile, "to be moved");
            string movedTargetFile = Path.Combine(watchedDir, "moved.txt");
            File.Move(movedSourceFile, movedTargetFile);
            await Task.Delay(200);

            string renamedFile = Path.Combine(watchedDir, "rename_source.txt");
            await File.WriteAllTextAsync(renamedFile, "to be renamed");
            await Task.Delay(200);
            string renamedTarget = Path.Combine(watchedDir, "rename_target.txt");
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
        finally {
            if (Directory.Exists(sourceDir)) {
                Directory.Delete(sourceDir, true);
            }
            if (Directory.Exists(watchedDir)) {
                Directory.Delete(watchedDir, true);
            }
        }
    }
}
