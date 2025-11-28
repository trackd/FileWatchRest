namespace FileWatchRest;

public static class ProgramHelpers {
    public static string GetExternalConfigPath(string[]? args, Func<string?>? envGetter = null, string? programData = null, Func<string, bool>? existsChecker = null) {
        envGetter ??= () => Environment.GetEnvironmentVariable("FILEWATCHREST_CONFIG");
        existsChecker ??= File.Exists;
        string? explicitConfig = null;

        if (args?.Length > 0) {
            for (int i = 0; i < args.Length; i++) {
                if (string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase) || string.Equals(args[i], "-c", StringComparison.OrdinalIgnoreCase)) {
                    if (i + 1 < args.Length && !string.IsNullOrWhiteSpace(args[i + 1])) {
                        explicitConfig = args[i + 1];
                        break;
                    }
                }
            }

            if (explicitConfig is null && args.Length > 0 && existsChecker(args[0])) {
                explicitConfig = args[0];
            }
        }

        if (string.IsNullOrWhiteSpace(explicitConfig)) {
            string? envCfg = envGetter();
            if (!string.IsNullOrWhiteSpace(envCfg)) {
                explicitConfig = envCfg;
            }
        }

        programData ??= Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string serviceDir = Path.Combine(programData, "FileWatchRest");
        return explicitConfig ?? Path.Combine(serviceDir, "FileWatchRest.json");
    }
}
