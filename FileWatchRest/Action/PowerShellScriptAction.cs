namespace FileWatchRest.Services;

public class PowerShellScriptAction(string scriptPath, List<string>? arguments = null, ILogger<PowerShellScriptAction>? logger = null) : IFolderAction {
    private readonly string _scriptPath = scriptPath;
    private readonly List<string>? _arguments = arguments;
    private readonly ILogger<PowerShellScriptAction> _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PowerShellScriptAction>.Instance;

    public async Task ExecuteAsync(FileEventRecord fileEvent, CancellationToken cancellationToken) {
        const string pwshExe = "pwsh.exe";
        const string powershellExe = "powershell.exe";
        string exePath = pwshExe;
        if (!File.Exists(pwshExe)) {
            exePath = powershellExe;
        }
        var args = new List<string>
        {
            "-File",
            _scriptPath,
            // Default input: JSON, output: XML
            // JsonSerializer.Serialize(fileEvent),
            "-OutputFormat",
            "XML"
        };
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
        var psi = new ProcessStartInfo {
            FileName = exePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string a in args) {
            psi.ArgumentList.Add(a);
        }
        using var process = Process.Start(psi);
        string? outputXml = null;
        if (process is not null) {
            outputXml = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            // Log the XML output if present
            if (!string.IsNullOrWhiteSpace(outputXml) && _logger.IsEnabled(LogLevel.Information)) {
                LoggerDelegates.PowerShellOutputXml(_logger, _scriptPath, outputXml, null);
            }
        }
    }
}
