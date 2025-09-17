using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using FileWatchRest.Configuration;
using FileWatchRest.Services;

var host = Host.CreateDefaultBuilder(args)
    .UseWindowsService()
    .ConfigureLogging((context, logging) => {
        // Add CSV logger provider for structured logs when running headless
        logging.AddProvider(new FileWatchRest.Logging.CsvLoggerProvider("FileWatchRest"));
    })
    .ConfigureServices((context, services) => {
        // All configuration is now handled through the single FileWatchRest.json file
        services.AddHttpClient();
        services.AddSingleton<DiagnosticsService>();
        services.AddSingleton<ConfigurationService>(provider =>
            new ConfigurationService(provider.GetRequiredService<ILogger<ConfigurationService>>(), "FileWatchRest"));
        services.AddHostedService<Worker>();
    })
    .Build();

host.Run();
