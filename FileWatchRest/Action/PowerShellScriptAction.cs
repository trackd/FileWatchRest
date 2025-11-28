namespace FileWatchRest.Services;

public class PowerShellScriptAction(
    string scriptPath,
    List<string>? arguments = null,
    ILogger<PowerShellScriptAction>? logger = null,
    int? executionTimeoutMilliseconds = null,
    bool ignoreOutput = false,
    Func<string, string?>? executableResolver = null
    ) : IFolderAction {
    private readonly string _scriptPath = scriptPath;
    private readonly List<string>? _arguments = arguments;
    private readonly ILogger<PowerShellScriptAction> _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PowerShellScriptAction>.Instance;
    private readonly int? _executionTimeoutMilliseconds = executionTimeoutMilliseconds;
    private readonly bool _ignoreOutput = ignoreOutput;
    private readonly Func<string, string?>? _executableResolver = executableResolver;
    public async Task ExecuteAsync(FileEventRecord fileEvent, CancellationToken cancellationToken) {
        // Build the ProcessStartInfo via the class-level factory so tests can inspect it.
        ProcessStartInfo psi = CreateProcessStartInfo(fileEvent);

        // If the configured script path does not exist at runtime, log an explicit not-found event and bail.
        if (!File.Exists(_scriptPath)) {
            if (_logger.IsEnabled(LogLevel.Error)) {
                var fnf = new FileNotFoundException($"Configured PowerShell script not found: {_scriptPath}", _scriptPath);
                LoggerDelegates.PowerShellScriptNotFound(_logger, _scriptPath, fnf);
            }
            return;
        }

        Process? process = null;
        try {
            process = Process.Start(psi);
        }
        catch (UnauthorizedAccessException uex) {
            if (_logger.IsEnabled(LogLevel.Error)) {
                LoggerDelegates.PowerShellAccessDenied(_logger, _scriptPath, uex);
            }
            return;
        }
        catch (Exception ex) {
            if (_logger.IsEnabled(LogLevel.Error)) {
                LoggerDelegates.PowerShellStartFailed(_logger, _scriptPath, ex);
            }
            return;
        }

        if (process is null) {
            if (_logger.IsEnabled(LogLevel.Warning)) {
                LoggerDelegates.PowerShellProcessNotStarted(_logger, _scriptPath, null);
            }
            return;
        }

        if (process is not null) {
            // Create a linked cancellation token that will cancel after the configured timeout
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (_executionTimeoutMilliseconds is int ms && ms > 0) linkedCts.CancelAfter(ms);

            string? outputXml = null;
            try {
                Task exitTask = process.WaitForExitAsync(linkedCts.Token);

                if (_ignoreOutput) {
                    // Not redirecting output when ignoring; just wait for exit.
                    await exitTask.ConfigureAwait(false);
                    outputXml = null;
                }
                else {
                    Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
                    Task<string> stderrTask = process.StandardError.ReadToEndAsync(linkedCts.Token);
                    await Task.WhenAll(stdoutTask, stderrTask, exitTask).ConfigureAwait(false);

                    // Combine stdout and stderr for logging purposes
                    StringBuilder sb = new();
                    string so = await stdoutTask.ConfigureAwait(false);
                    string se = await stderrTask.ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(so)) sb.AppendLine(so);
                    if (!string.IsNullOrEmpty(se)) sb.AppendLine(se);
                    outputXml = sb.Length > 0 ? sb.ToString() : null;
                }
            }
            catch (OperationCanceledException) {
                try {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                }
                catch { }
                if (_logger.IsEnabled(LogLevel.Warning)) {
                    LoggerDelegates.PowerShellTimeout(_logger, _scriptPath, _executionTimeoutMilliseconds ?? 0, null);
                }
                return;
            }

            // Log exit code
            if (_logger.IsEnabled(LogLevel.Information)) {
                try { LoggerDelegates.PowerShellExitCode(_logger, _scriptPath, process.ExitCode, null); } catch { }
            }

            if (!string.IsNullOrWhiteSpace(outputXml) && _logger.IsEnabled(LogLevel.Information)) {
                LoggerDelegates.PowerShellOutputXml(_logger, _scriptPath, outputXml, null);
            }
        }
    }

    internal ProcessStartInfo CreateProcessStartInfo(FileEventRecord fileEvent) {
        const string powershellExe = "powershell.exe";
        var args = new List<string>
        {
            "-NoLogo",
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-NonInteractive",
            "-OutputFormat",
            "XML",
            "-File",
            _scriptPath
        };
        if (_arguments is not null) {
            foreach (string arg in _arguments) {
                if (arg == "{FilePath}") args.Add(fileEvent.Path);
                else if (arg == "{FileNotification:json}") args.Add(JsonSerializer.Serialize(fileEvent, MyJsonContext.Default.FileEventRecord));
                else args.Add(arg);
            }
        }

        var psi = new ProcessStartInfo {
            FileName = _executableResolver is not null ? (_executableResolver("pwsh") ?? powershellExe) : (TryFindExecutableInPath("pwsh", out string pwshFullPath) ? pwshFullPath : powershellExe),
            RedirectStandardOutput = !_ignoreOutput,
            RedirectStandardError = !_ignoreOutput,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string a in args) psi.ArgumentList.Add(a);
        psi.WorkingDirectory = Path.GetDirectoryName(_scriptPath) ?? AppContext.BaseDirectory;
        return psi;
    }

    private static bool TryFindExecutableInPath(string exeName, out string fullPath) {
        fullPath = string.Empty;
        if (Path.IsPathRooted(exeName)) {
            if (File.Exists(exeName)) {
                fullPath = exeName;
                return true;
            }
            return false;
        }

        string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (string.IsNullOrEmpty(path)) return false;
        string pathext = Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT";
        string[] exts = pathext.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            try {
                foreach (string ext in exts) {
                    string candidate = Path.Combine(dir, Path.HasExtension(exeName) ? exeName : exeName + ext);
                    if (File.Exists(candidate)) {
                        fullPath = candidate;
                        return true;
                    }
                }
            }
            catch {
                // ignore malformed PATH entries
            }
        }

        return false;
    }
}
