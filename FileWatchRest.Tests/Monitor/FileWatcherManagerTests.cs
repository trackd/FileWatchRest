namespace FileWatchRest.Tests;

public class FileWatcherManagerTests {
    [Fact]
    public async Task WatcherErrorTriggersExceededCallbackIntegration() {
        ILoggerFactory factory = LoggerFactory.Create(b => b.AddDebug());
        var diag = new DiagnosticsService(factory.CreateLogger<DiagnosticsService>(), new OptionsMonitorMock<ExternalConfiguration>());
        var manager = new FileWatcherManager(factory.CreateLogger<FileWatcherManager>(), diag);

        string tempDir = Path.Combine(Path.GetTempPath(), $"fwtest_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        var config = new ExternalConfiguration {
            WatcherMaxRestartAttempts = 2,
            WatcherRestartDelayMilliseconds = 10,
            IncludeSubdirectories = false,
            FileWatcherInternalBufferSize = 4096,
            AllowedExtensions = []
        };

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Use a handler that does nothing, just for wiring
        void dummyHandler(object s, FileSystemEventArgs e) { }
        void errorHandler(string folder, ErrorEventArgs e) { }
        void exceededHandler(string folder) {
            tcs.TrySetResult(true);
        }

        await manager.StartWatchingAsync([new ExternalConfiguration.WatchedFolderConfig { FolderPath = tempDir }], config, (folder, e, cfg, act) => dummyHandler(manager, e), errorHandler, exceededHandler);

        // Try to trigger watcher errors; OS behavior is not reliable in test environments,
        // so invoke the manager's error handler reflectively to simulate repeated errors
        MethodInfo? mi = manager.GetType().GetMethod("HandleWatcherError", BindingFlags.NonPublic | BindingFlags.Instance);
        if (mi != null) {
            var errorArgs = new ErrorEventArgs(new InvalidOperationException("simulated"));
            // Build a context-aware adapter for the dummy handler to match the new signature
            Action<string, FileSystemEventArgs, ExternalConfiguration?, IFolderAction?> ctxAdapter = (f, fe, cfg, act) => dummyHandler(manager, fe);
            // Invoke enough times to exceed the configured restart attempts
            for (int i = 0; i < config.WatcherMaxRestartAttempts + 1; i++) {
                mi.Invoke(manager, [tempDir, errorArgs, ctxAdapter, (Action<string, ErrorEventArgs>?)errorHandler, (Action<string>?)exceededHandler]);
            }
        }

        Task completed = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        Assert.Same(tcs.Task, completed);

        Directory.Delete(tempDir, true);
    }
}
