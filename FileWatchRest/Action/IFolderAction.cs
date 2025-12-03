namespace FileWatchRest.Services;

public interface IFolderAction {
    Task ExecuteAsync(FileEventRecord fileEvent, CancellationToken cancellationToken);
}
