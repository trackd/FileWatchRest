namespace FileWatchRest;

/// <summary>
/// Encapsulates configuration path resolution logic from command-line arguments and environment variables.
/// </summary>
public static class ConfigurationPathResolver {
    /// <summary>
    /// Resolve configuration file path using the following priority:
    /// 1. Command-line: --config <path> or -c <path>
    /// 2. First positional argument (if it's an existing file)
    /// 3. Environment variable FILEWATCHREST_CONFIG
    /// 4. Default: {CommonApplicationData}/FileWatchRest/FileWatchRest.json
    /// </summary>
    public static string ResolveConfigurationPath(string[]? args = null) {
        // Check command-line arguments
        if (args?.Length > 0) {
            string? explicitConfig = ParseCommandLineConfig(args);
            if (explicitConfig is not null) {
                return explicitConfig;
            }
        }

        // Check environment variable
        string? envCfg = Environment.GetEnvironmentVariable("FILEWATCHREST_CONFIG");
        if (!string.IsNullOrWhiteSpace(envCfg)) {
            return envCfg;
        }

        // Use default
        return GetDefaultConfigurationPath();
    }

    /// <summary>
    /// Get the default configuration path under CommonApplicationData.
    /// </summary>
    public static string GetDefaultConfigurationPath() {
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string serviceDir = Path.Combine(programData, "FileWatchRest");
        return Path.Combine(serviceDir, "FileWatchRest.json");
    }

    /// <summary>
    /// Get the base directory where logs and other artifacts should be stored.
    /// Prefers the directory containing the configuration file, falls back to the service directory.
    /// </summary>
    public static string GetBaseDirectory(string configPath) {
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string serviceDir = Path.Combine(programData, "FileWatchRest");

        // Create service directory if it doesn't exist
        try {
            Directory.CreateDirectory(serviceDir);
        }
        catch {
            // Best-effort
        }

        // Prefer config directory if available, fall back to service directory
        string? configDir = Path.GetDirectoryName(configPath);
        return !string.IsNullOrWhiteSpace(configDir) && Directory.Exists(configDir) ? configDir : serviceDir;
    }

    /// <summary>
    /// Parse --config or -c argument from command-line args, or accept first positional arg if it's a file.
    /// Returns null if no config path is found in args.
    /// </summary>
    private static string? ParseCommandLineConfig(string[] args) {
        // Look for --config or -c flag with value
        for (int i = 0; i < args.Length; i++) {
            if ((args[i].Equals("--config", StringComparison.OrdinalIgnoreCase) ||
                args[i].Equals("-c", StringComparison.OrdinalIgnoreCase)) &&
                i + 1 < args.Length &&
                !string.IsNullOrWhiteSpace(args[i + 1])) {
                return args[i + 1];
            }
        }

        // Accept first positional argument if it's an existing file
        return args.Length > 0 && File.Exists(args[0]) ? args[0] : null;
    }
}
