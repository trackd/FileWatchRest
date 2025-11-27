// Resolve external config path. Priority:
// 1) Command-line: --config <path> or -c <path> or first positional arg
// 2) Environment variable FILEWATCHREST_CONFIG
// 3) Default under CommonApplicationData
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

    if (explicitConfig is null && args.Length > 0 && File.Exists(args[0])) {
        explicitConfig = args[0];
    }
}

if (string.IsNullOrWhiteSpace(explicitConfig)) {
    string? envCfg = Environment.GetEnvironmentVariable("FILEWATCHREST_CONFIG");
    if (!string.IsNullOrWhiteSpace(envCfg)) {
        explicitConfig = envCfg;
    }
}

// Compute program data/service dir once only if we need the default path or for logging
string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
string serviceDir = Path.Combine(programData, "FileWatchRest");
string externalConfigPath = explicitConfig ?? Path.Combine(serviceDir, "FileWatchRest.json");

SimpleFileLoggerOptions? loggingOptions = null;
if (File.Exists(externalConfigPath)) {
    try {
        string json = File.ReadAllText(externalConfigPath);
        ExternalConfiguration? extConfig = JsonSerializer.Deserialize(json, MyJsonContext.Default.ExternalConfiguration);
        if (extConfig is not null) {
            loggingOptions = extConfig.Logging;
        }
    }
    catch {
        // Ignore - will use defaults below
    }
}

loggingOptions ??= new SimpleFileLoggerOptions {
    LogType = LogType.Csv,
    FilePathPattern = "logs/FileWatchRest_{0:yyyyMMdd_HHmmss}",
    LogLevel = LogLevel.Information,
    // RetainedFileCountLimit is obsolete and removed. Use RetainedDays for retention policy.
};

// Ensure logs directory exists and resolve file paths relative to CommonApplicationData when not absolute
try {
    _ = Directory.CreateDirectory(serviceDir);
    string ResolvePath(string p) {
        return string.IsNullOrWhiteSpace(p)
            ? Path.Combine(serviceDir, "logs", "FileWatchRest.log")
            : Path.IsPathRooted(p) ? p : Path.Combine(serviceDir, p.Replace('/', Path.DirectorySeparatorChar));
    }

    string baseResolved = ResolvePath(string.Format(CultureInfo.InvariantCulture, loggingOptions.FilePathPattern, DateTime.Now));
    string CsvLogFilePath(string basePath) {
        return Path.HasExtension(basePath) ? basePath : basePath + ".csv";
    }
    string JsonLogFilePath(string basePath) {
        return Path.HasExtension(basePath) ? basePath : basePath + ".json";
    }

    string csvLogFile = CsvLogFilePath(baseResolved);
    string jsonLogFile = JsonLogFilePath(baseResolved);

    // Ensure CSV header if CSV sink will be used
    if (loggingOptions.LogType is LogType.Csv or LogType.Both) {
        try {
            if (!File.Exists(csvLogFile)) {
                File.WriteAllText(csvLogFile, "Timestamp,Level,Message,Category,Exception,StatusCode\n");
            }
        }
        catch { }
    }

    // Build simple file logger options (AOT-safe): map unified options
    var fileLoggerOptions = new SimpleFileLoggerOptions {
        LogType = loggingOptions.LogType,
        FilePathPattern = loggingOptions.FilePathPattern,
        // RetainedFileCountLimit is obsolete and removed. Use RetainedDays for retention policy.
        LogLevel = loggingOptions.LogLevel
    };

    IHost host = new HostBuilder()
        .UseWindowsService()
        .ConfigureLogging((context, logging) => {
            logging.ClearProviders();
            // Set minimum level to Trace so CSV logger receives all messages
            logging.SetMinimumLevel(LogLevel.Trace);
            // Use custom provider only; it will emit both file and concise console output.
            logging.AddProvider(new SimpleFileLoggerProvider(fileLoggerOptions));
        })
        .ConfigureServices((context, services) => {
            // Register a typed HttpClient for the file API.
            // Retry logic is implemented in the Worker (so the HTTP client is a plain client here).
            services.AddHttpClient("fileApi")
                .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(30));

            // Register other services
            // All configuration is now handled through the single FileWatchRest.json file
            services.AddHttpClient();

            // Register services with interfaces for testability (SOLID principles)
            services.AddSingleton<DiagnosticsService>();
            services.AddSingleton<IDiagnosticsService>(provider => provider.GetRequiredService<DiagnosticsService>());

            // Register the ExternalConfigurationOptionsMonitor as a concrete singleton so DI can construct it.
            services.AddSingleton<ExternalConfigurationOptionsMonitor>();
            services.AddSingleton<IOptionsMonitor<ExternalConfiguration>>(provider => provider.GetRequiredService<ExternalConfigurationOptionsMonitor>());

            services.AddSingleton<IResilienceService, HttpResilienceService>();

            services.AddSingleton<FileWatcherManager>();
            services.AddSingleton<IFileWatcherManager>(provider => provider.GetRequiredService<FileWatcherManager>());

            // Register channel for debounce -> sender communication
            services.AddSingleton(Channel.CreateBounded<string>(new BoundedChannelOptions(1000) {
                FullMode = BoundedChannelFullMode.Wait
            }));
            services.AddSingleton(provider => provider.GetRequiredService<Channel<string>>().Writer);
            services.AddSingleton(provider => provider.GetRequiredService<Channel<string>>().Reader);

            // Register background services for debouncing and sending
            services.AddSingleton(provider => {
                ILogger<FileDebounceService> logger = provider.GetRequiredService<ILogger<FileDebounceService>>();
                ChannelWriter<string> writer = provider.GetRequiredService<ChannelWriter<string>>();
                IOptionsMonitor<ExternalConfiguration> optionsMonitor = provider.GetRequiredService<IOptionsMonitor<ExternalConfiguration>>();
                return new FileDebounceService(logger, writer, () => optionsMonitor.CurrentValue);
            });
            services.AddHostedService(provider => provider.GetRequiredService<FileDebounceService>());

            // Register Worker first so FileSenderService can access it
            services.AddSingleton<Worker>();
            services.AddHostedService(provider => provider.GetRequiredService<Worker>());

            services.AddSingleton(provider => {
                ILogger<FileSenderService> logger = provider.GetRequiredService<ILogger<FileSenderService>>();
                ChannelReader<string> reader = provider.GetRequiredService<ChannelReader<string>>();
                Worker worker = provider.GetRequiredService<Worker>();
                return new FileSenderService(logger, reader, worker.ProcessFileAsync);
            });
            services.AddHostedService(provider => provider.GetRequiredService<FileSenderService>());
        })
        .Build();

    host.Run();
}
catch (Exception ex) {
    try { Console.Error.WriteLine($"Host terminated unexpectedly: {ex}"); } catch { }
    throw;
}
