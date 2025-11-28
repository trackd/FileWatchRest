namespace FileWatchRest.Services;

public class ExecutableAction(string executablePath, List<string>? arguments) : IFolderAction {
    private readonly string _executablePath = executablePath;
    private readonly List<string>? _arguments = arguments;
    // Test hook: when set, this delegate will be invoked instead of actually starting a process.
    // Signature: (startInfo, cancellationToken) => Task
    internal static Func<ProcessStartInfo, CancellationToken, Task>? StartProcessAsyncOverride;

    public async Task ExecuteAsync(FileEventRecord fileEvent, CancellationToken cancellationToken) {
        var args = new List<string>();
        if (_arguments is not null) {
            foreach (string arg in _arguments) {
                if (arg == "{FilePath}") {
                    args.Add(fileEvent.Path);
                }
                else if (arg == "{FileNotification:json}") {
                    args.Add(JsonSerializer.Serialize(fileEvent, MyJsonContext.Default.FileEventRecord));
                }
                else {
                    args.Add(arg);
                }
            }
        }
        var exec = new ProcessStartInfo(_executablePath) {
            UseShellExecute = false
        };
        foreach (string a in args) {
            exec.ArgumentList.Add(a);
        }

        if (StartProcessAsyncOverride is not null) {
            await StartProcessAsyncOverride(exec, cancellationToken).ConfigureAwait(false);
            return;
        }

        using var proc = Process.Start(exec);
        if (proc is not null) {
            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
