// Build configuration early so we can read logging options
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

// Resolve external config (primary source) and load logging options from it if present.
var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
var serviceDir = Path.Combine(programData, "FileWatchRest");
var externalConfigPath = Path.Combine(serviceDir, "FileWatchRest.json");

LoggingOptions? loggingOptions = null;
if (File.Exists(externalConfigPath))
{
    try
    {
        var extConfig = ConfigurationService.LoadFromFile(externalConfigPath);
        if (extConfig is not null)
            loggingOptions = extConfig.Logging;
    }
    catch
    {
        // Ignore - we'll fall back to appsettings.json below
    }
}

// Fallback to appsettings LoggingOptions if external config absent
if (loggingOptions is null)
{
    try
    {
        var loggingSection = configuration.GetSection("LoggingOptions");
        if (loggingSection.Exists())
        {
            // Read new unified settings first
            var logTypeRaw = loggingSection["LogType"]; var logType = FileWatchRest.Configuration.LogType.Csv;
            if (!string.IsNullOrEmpty(logTypeRaw) && Enum.TryParse<FileWatchRest.Configuration.LogType>(logTypeRaw, true, out var lt)) logType = lt;

            var filePatternRaw = loggingSection["FilePathPattern"]; var filePattern = string.IsNullOrEmpty(filePatternRaw) ? null : filePatternRaw;

            var logLevelRaw = loggingSection["LogLevel"]; var minLevel = string.IsNullOrEmpty(logLevelRaw) ? "Information" : logLevelRaw;
            var retainedRaw = loggingSection["RetainedFileCountLimit"]; var retained = 14; if (!string.IsNullOrEmpty(retainedRaw) && int.TryParse(retainedRaw, out var rtv)) retained = rtv;

            loggingOptions = new LoggingOptions
            {
                LogType = logType,
                FilePathPattern = filePattern ?? "logs/FileWatchRest_{0:yyyyMMdd_HHmmss}",
                LogLevel = minLevel!,
                RetainedFileCountLimit = retained
            };
        }
        else
        {
            loggingOptions = new LoggingOptions();
        }
    }
    catch
    {
        // Fallback to defaults
        loggingOptions = new LoggingOptions();
    }
}

// Ensure logs directory exists and resolve file paths relative to CommonApplicationData when not absolute
try
{
    Directory.CreateDirectory(serviceDir);
    string ResolvePath(string p)
    {
        if (string.IsNullOrWhiteSpace(p)) return Path.Combine(serviceDir, "logs", "FileWatchRest.log");
        if (Path.IsPathRooted(p)) return p;
        return Path.Combine(serviceDir, p.Replace('/', Path.DirectorySeparatorChar));
    }

    var baseResolved = ResolvePath(string.Format(loggingOptions.FilePathPattern, DateTime.Now));
    string CsvLogFilePath(string basePath)
    {
        return Path.HasExtension(basePath) ? basePath : basePath + ".csv";
    }
    string JsonLogFilePath(string basePath)
    {
        return Path.HasExtension(basePath) ? basePath : basePath + ".ndjson";
    }

    var csvLogFile = CsvLogFilePath(baseResolved);
    var jsonLogFile = JsonLogFilePath(baseResolved);

    // Ensure CSV header if CSV sink will be used
    if (loggingOptions.LogType == FileWatchRest.Configuration.LogType.Csv || loggingOptions.LogType == FileWatchRest.Configuration.LogType.Both)
    {
        try { if (!File.Exists(csvLogFile)) File.WriteAllText(csvLogFile, "Timestamp,Level,Message,Category,Exception,StatusCode\n"); } catch { }
    }

    // Build simple file logger options (AOT-safe): map unified options
    var fileLoggerOptions = new SimpleFileLoggerOptions
    {
        LogType = loggingOptions.LogType,
        FilePathPattern = loggingOptions.FilePathPattern,
        RetainedFileCountLimit = loggingOptions.RetainedFileCountLimit,
        LogLevel = Enum.TryParse<LogLevel>(loggingOptions.LogLevel, true, out var ml) ? ml : LogLevel.Information
    };

    // Use console to surface early startup messages, file provider will be added to host logging below.
    Console.WriteLine("Starting FileWatchRest host");

    var host = Host.CreateDefaultBuilder(args)
        .UseWindowsService()
        .ConfigureLogging((context, logging) =>
        {
            logging.ClearProviders();
            // Use custom provider only; it will emit both file and concise console output.
            logging.AddProvider(new SimpleFileLoggerProvider(fileLoggerOptions));
        })
        .ConfigureServices((context, services) => {
            // Register a typed HttpClient for the file API.
            // Retry logic is implemented in the Worker (so the HTTP client is a plain client here).
            services.AddHttpClient("fileApi")
                .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(60));

            // Register other services
            // All configuration is now handled through the single FileWatchRest.json file
            services.AddHttpClient();
            services.AddSingleton<DiagnosticsService>();
            services.AddSingleton<ConfigurationService>(provider =>
                new ConfigurationService(provider.GetRequiredService<ILogger<ConfigurationService>>(), "FileWatchRest"));

            // Expose the external configuration through the standard Options monitor pattern
            services.AddSingleton<IOptionsMonitor<ExternalConfiguration>, ExternalConfigurationOptionsMonitor>();

            services.AddSingleton<IResilienceService, HttpResilienceService>();
            services.AddSingleton<FileWatcherManager>();
            services.AddHostedService<Worker>();
        })
        .Build();

    host.Run();
}
catch (Exception ex)
{
    try { Console.Error.WriteLine($"Host terminated unexpectedly: {ex}"); } catch { }
    throw;
}
