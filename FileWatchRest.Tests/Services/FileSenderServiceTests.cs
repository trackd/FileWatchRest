namespace FileWatchRest.Tests.Services;

public class FileSenderServiceTests {
    [Fact]
    public async Task ExecuteAsync_processes_all_items_from_channel_then_exits() {
        var writer = Channel.CreateUnbounded<string>();
        ChannelReader<string> reader = writer.Reader;
        int processed = 0;

        ValueTask processFileAsync(string path, CancellationToken ct) {
            Interlocked.Increment(ref processed);
            return ValueTask.CompletedTask;
        }

        var svc = new FileSenderService(NullLogger<FileSenderService>.Instance, reader, processFileAsync);

        // Write some items then complete the writer so ExecuteAsync will finish
        await writer.Writer.WriteAsync("a");
        await writer.Writer.WriteAsync("b");
        writer.Writer.Complete();

        MethodInfo exec = typeof(FileSenderService).GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task)exec.Invoke(svc, [CancellationToken.None])!;
        await task;

        Assert.Equal(2, processed);
    }
}
