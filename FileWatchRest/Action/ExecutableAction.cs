namespace FileWatchRest.Services;

public class ExecutableAction(string executablePath, List<string>? arguments, ILogger<ExecutableAction>? logger = null, int? executionTimeoutMilliseconds = null, bool ignoreOutput = false) : IFolderAction {
    private readonly string _executablePath = executablePath;
    private readonly List<string>? _arguments = arguments;
    private readonly ILogger<ExecutableAction> _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ExecutableAction>.Instance;
    private readonly int? _executionTimeoutMilliseconds = executionTimeoutMilliseconds;
    private readonly bool _ignoreOutput = ignoreOutput;
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

        ProcessStartInfo exec = CreateProcessStartInfo(fileEvent);

        Process? proc = null;
        try {
            proc = Process.Start(exec);
        }
        catch (UnauthorizedAccessException uex) {
            if (_logger.IsEnabled(LogLevel.Error)) {
                LoggerDelegates.ExecutableAccessDenied(_logger, _executablePath, uex);
            }
            return;
        }
        catch (FileNotFoundException fnf) {
            // Distinct log for missing executable; include attempted path
            if (_logger.IsEnabled(LogLevel.Error)) {
                LoggerDelegates.ExecutableNotFound(_logger, _executablePath, fnf);
            }
            return;
        }
        catch (Exception ex) {
            if (_logger.IsEnabled(LogLevel.Error)) {
                LoggerDelegates.ExecutableStartFailed(_logger, _executablePath, ex);
            }
            return;
        }

        if (proc is null) return;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_executionTimeoutMilliseconds is int ms && ms > 0) linkedCts.CancelAfter(ms);

        try {
            Task exitTask = proc.WaitForExitAsync(linkedCts.Token);
            if (_ignoreOutput) {
                // Not redirecting output when ignoring; just wait for exit.
                await exitTask.ConfigureAwait(false);
                if (_logger.IsEnabled(LogLevel.Information)) {
                    LoggerDelegates.ExecutableExitCode(_logger, _executablePath, proc.ExitCode, null);
                }
            }
            else {
                Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync(linkedCts.Token);
                Task<string> stderrTask = proc.StandardError.ReadToEndAsync(linkedCts.Token);
                await Task.WhenAll(stdoutTask, stderrTask, exitTask).ConfigureAwait(false);
                StringBuilder sb = new();
                string so = await stdoutTask.ConfigureAwait(false);
                string se = await stderrTask.ConfigureAwait(false);
                if (!string.IsNullOrEmpty(so)) sb.AppendLine(so);
                if (!string.IsNullOrEmpty(se)) sb.AppendLine(se);
                string? combined = sb.Length > 0 ? sb.ToString() : null;
                if (!string.IsNullOrWhiteSpace(combined) && _logger.IsEnabled(LogLevel.Information)) {
                    LoggerDelegates.ExecutableOutput(_logger, _executablePath, combined, null);
                }
                if (_logger.IsEnabled(LogLevel.Information)) {
                    LoggerDelegates.ExecutableExitCode(_logger, _executablePath, proc.ExitCode, null);
                }
            }
        }
        catch (OperationCanceledException) {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            if (_logger.IsEnabled(LogLevel.Warning)) {
                LoggerDelegates.ExecutableTimeout(_logger, _executablePath, _executionTimeoutMilliseconds ?? 0, null);
            }
        }
        finally {
            try { proc.Dispose(); } catch { }
        }
    }
    internal ProcessStartInfo CreateProcessStartInfo(FileEventRecord fileEvent) {
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
            UseShellExecute = false,
            RedirectStandardOutput = !_ignoreOutput,
            RedirectStandardError = !_ignoreOutput,
            CreateNoWindow = true
        };
        foreach (string a in args) exec.ArgumentList.Add(a);
        return exec;
    }
}
