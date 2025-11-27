namespace FileWatchRest.Tests.Integration;

public class DiagnosticsApiIntegrationTests {
    [Fact]
    public async Task DiagnosticsApiReturnsCurrentConfig() {
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
            await Task.Delay(500);
        }
        if (lastEx != null) {
            throw new InvalidOperationException($"Diagnostics API not available after 10s. Ensure FileWatchRest service is running. Last error: {lastEx.Message}", lastEx);
        }

        // Act: Query the diagnostics API
        HttpResponseMessage response = await http.GetAsync(diagnosticsUrl);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync();

        // Assert: Check for expected config properties
        Assert.Contains("ApiEndpoint", json);
        Assert.Contains("Folders", json);
    }
}
