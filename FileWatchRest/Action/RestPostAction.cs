namespace FileWatchRest.Services;

public class RestPostAction(Worker worker) : IFolderAction {
    private readonly Worker _worker = worker;

    public async Task ExecuteAsync(FileEventRecord fileEvent, CancellationToken cancellationToken) {
        // Enqueue via the Worker's debounced path so Created/Changed events collapse
        // into a single processing operation rather than invoking processing directly.
        _worker.EnqueueFileFromAction(fileEvent.Path);
        await Task.CompletedTask;
    }
}
