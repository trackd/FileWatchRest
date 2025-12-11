using FileWatchRest;
using FileWatchRest.Configuration;
using FileWatchRest.Logging;
using FileWatchRest.Models;
using FileWatchRest.Services;
using Microsoft.Extensions.Hosting.WindowsServices;
using System.Threading.Channels;

try {
    // Resolve configuration paths
    string configPath = ConfigurationPathResolver.ResolveConfigurationPath(args);
    string baseDir = ConfigurationPathResolver.GetBaseDirectory(configPath);

    IHost host = new HostBuilder()
        .UseWindowsService()
        .ConfigureFileLogging(configPath, baseDir)
        .ConfigureServices((context, services) => {
            // Register a typed HttpClient for the file API.
            // Retry logic is implemented in the Worker (so the HTTP client is a plain client here).
            services.AddHttpClient("fileApi")
                .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(30));

            // Register other services
            // All configuration is now handled through the single FileWatchRest.json file
            services.AddHttpClient();

            // Register services with interfaces for testability
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

    // Log startup configuration
    ILogger<Program> logger = host.Services.GetRequiredService<ILogger<Program>>();
    IOptionsMonitor<ExternalConfiguration> configMonitor = host.Services.GetRequiredService<IOptionsMonitor<ExternalConfiguration>>();
    ExternalConfiguration config = configMonitor.CurrentValue ?? new ExternalConfiguration();
    LoggingConfigurationHelper.LogStartupConfiguration(logger, config, configPath);

    host.Run();
}
catch (Exception ex) {
    try {
        Console.Error.WriteLine($"Host terminated unexpectedly: {ex}");
    }
    catch {
        // Ignored
    }

    throw;
}
