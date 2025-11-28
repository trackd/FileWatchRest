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
        FileSystemEventHandler dummyHandler = (s, e) => { };
        void errorHandler(string folder, ErrorEventArgs e) { }
        void exceededHandler(string folder) {
            tcs.TrySetResult(true);
        }

        await manager.StartWatchingAsync([tempDir], config, dummyHandler, errorHandler, exceededHandler);

        // Try to trigger watcher errors; OS behavior is not reliable in test environments,
        // so invoke the manager's error handler reflectively to simulate repeated errors
        System.Reflection.MethodInfo? mi = manager.GetType().GetMethod("HandleWatcherError", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (mi != null) {
            var errorArgs = new ErrorEventArgs(new InvalidOperationException("simulated"));
            // Invoke enough times to exceed the configured restart attempts
            for (int i = 0; i < config.WatcherMaxRestartAttempts + 1; i++) {
                mi.Invoke(manager, [tempDir, errorArgs, dummyHandler, (Action<string, ErrorEventArgs>?)errorHandler, (Action<string>?)exceededHandler]);
            }
        }

        Task completed = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        completed.Should().Be(tcs.Task);

        Directory.Delete(tempDir, true);
    }
}
