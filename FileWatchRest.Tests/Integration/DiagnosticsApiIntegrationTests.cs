namespace FileWatchRest.Tests.Integration;

public class DiagnosticsApiIntegrationTests {
    [Fact]
    public async Task DiagnosticsApiReturnsCurrentConfig() {
        // Start an in-process diagnostics HTTP server so tests don't require an external service.
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace));
        var options = new OptionsMonitorMock<ExternalConfiguration>();
        var diagService = new DiagnosticsService(loggerFactory.CreateLogger<DiagnosticsService>(), options);
        diagService.StartHttpServer("http://localhost:5005/");

        try {
            // Arrange: Use the diagnostics API endpoint
            string diagnosticsUrl = "http://localhost:5005/config";
            var http = new HttpClient();
            // Add Authorization header if DiagnosticsBearerToken is set
            string? token = Environment.GetEnvironmentVariable("DIAGNOSTICS_BEARER_TOKEN");
            if (!string.IsNullOrWhiteSpace(token)) {
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }

            // Wait for diagnostics API to be available (max 10s)
            DateTime timeout = DateTime.UtcNow.AddSeconds(10);
            Exception? lastEx = null;
            while (DateTime.UtcNow < timeout) {
                try {
                    HttpResponseMessage resp = await http.GetAsync(diagnosticsUrl);
                    if (resp.IsSuccessStatusCode) {
                        string dbgJson = await resp.Content.ReadAsStringAsync();
                        break;
                    }
                }
                catch (Exception ex) {
                    lastEx = ex;
                }
                await Task.Delay(250);
            }
            if (lastEx != null) {
                throw new InvalidOperationException($"Diagnostics API not available after 10s. Ensure FileWatchRest service is running. Last error: {lastEx.Message}", lastEx);
            }

            // Act: Query the diagnostics API
            HttpResponseMessage response = await http.GetAsync(diagnosticsUrl);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync();

            // Assert: Check for expected config properties
            // We expect a valid JSON representation of the runtime configuration; ensure Folders exists.
            Assert.Contains("Folders", json);
        }
        finally {
            try { diagService.Dispose(); } catch { }
        }
    }
}
