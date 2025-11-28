namespace FileWatchRest.Tests.Configuration;

public class ExternalConfigurationSaveTests {
    [Fact]
    public void ExampleConfig_RoundTripsActionsUnchanged() {
        // Locate the repository examples directory by walking up parent directories
        string? probe = AppContext.BaseDirectory;
        string? examplesDir = null;
        for (int i = 0; i < 10 && !string.IsNullOrEmpty(probe); i++) {
            string candidate = Path.Combine(probe, "examples");
            if (Directory.Exists(candidate)) { examplesDir = Path.GetFullPath(candidate); break; }
            string? parent = Path.GetDirectoryName(probe);
            probe = parent;
        }
        examplesDir.Should().NotBeNull("examples directory must exist for this test");

        // Pick the first example JSON file and copy to temp path so the monitor may write/migrate safely during the test
        string exampleFile = Directory.GetFiles(examplesDir!, "*.json").OrderBy(n => n).First();
        string tmpDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tmpDir);
        string tempConfig = Path.Combine(tmpDir, Path.GetFileName(exampleFile));
        File.Copy(exampleFile, tempConfig);

        NullLogger<ExternalConfigurationOptionsMonitor> logger = NullLogger<ExternalConfigurationOptionsMonitor>.Instance;
        NullLoggerFactory lf = NullLoggerFactory.Instance;
        var monitor = new ExternalConfigurationOptionsMonitor(tempConfig, logger, lf);

        // Read original actions and saved actions and compare per-action property sets
        using var origDoc = JsonDocument.Parse(File.ReadAllText(exampleFile));
        using var savedDoc = JsonDocument.Parse(File.ReadAllText(tempConfig));

        origDoc.RootElement.TryGetProperty("Actions", out JsonElement origActions).Should().BeTrue();
        savedDoc.RootElement.TryGetProperty("Actions", out JsonElement savedActions).Should().BeTrue();

        origActions.GetArrayLength().Should().Be(savedActions.GetArrayLength());

        for (int i = 0; i < origActions.GetArrayLength(); i++) {
            JsonElement o = origActions[i];
            JsonElement s = savedActions[i];

            // Build name->value map for properties present in original
            var origMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty p in o.EnumerateObject()) origMap[p.Name] = p.Value.GetRawText();

            var savedMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty p in s.EnumerateObject()) savedMap[p.Name] = p.Value.GetRawText();

            // Ensure every property present in original exists with same raw text in saved
            foreach (KeyValuePair<string, string> kv in origMap) {
                savedMap.Should().ContainKey(kv.Key);
                savedMap[kv.Key].Should().Be(kv.Value, because: $"property '{kv.Key}' should round-trip unchanged for action index {i}");
            }

            // Also ensure saved does not introduce unexpected properties that were not present in original
            foreach (KeyValuePair<string, string> kv in savedMap) {
                origMap.ContainsKey(kv.Key).Should().BeTrue(because: $"saved action must not introduce new property '{kv.Key}' for action index {i}");
            }
        }
    }
}
