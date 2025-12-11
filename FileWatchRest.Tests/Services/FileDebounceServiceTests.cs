namespace FileWatchRest.Tests.Services;

public class FileDebounceServiceTests {
    [Fact]
    public async Task ExecuteAsync_writes_scheduled_file_to_output_channel_after_debounce() {
        var channel = Channel.CreateUnbounded<string>();
        var config = new ExternalConfiguration { DebounceMilliseconds = 0 };
        ExternalConfiguration GetConfig() {
            return config;
        }

        var svc = new FileDebounceService(NullLogger<FileDebounceService>.Instance, channel.Writer, GetConfig);

        // Schedule a file and run the service briefly
        svc.Schedule("file1.txt");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await svc.StartAsync(CancellationToken.None);

        // Attempt to read output (should be produced quickly since DebounceMilliseconds = 0)
        Task<string> readTask = channel.Reader.ReadAsync(cts.Token).AsTask();
        Task completed = await Task.WhenAny(readTask, Task.Delay(1500, cts.Token));

        Assert.Same(readTask, completed);
        string result = await readTask;
        Assert.Equal("file1.txt", result);

        // Stop service
        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }
}
