namespace FileWatchRest.Tests;

public class SimpleFileLoggerProviderStructuredFieldsTests {
    [Fact]
    public void LoggerEmitsStatusCodeInJsonAndCsv() {
        string guidDir = Guid.NewGuid().ToString();
        string tempDir = Path.Combine(Path.GetTempPath(), "FileWatchRest_TestLogs", guidDir);
        Directory.CreateDirectory(tempDir);
        string basePath = Path.Combine(tempDir, "testlog");

        try {
            var options = new SimpleFileLoggerOptions {
                LogType = LogType.Both,
                FilePathPattern = basePath,
            };

            var provider = new SimpleFileLoggerProvider(options);
            ILogger logger = provider.CreateLogger("UnitTest");

            if (logger.IsEnabled(LogLevel.Information)) {
                LoggerDelegates.UploadResult(logger, "file.txt", 201, null);
            }

            provider.Dispose();

            // Wait briefly to ensure all file buffers are flushed
            Thread.Sleep(100);

            string ndjsonPath = basePath + ".json";
            string csvPath = basePath + ".csv";

            File.Exists(ndjsonPath).Should().BeTrue();
            File.Exists(csvPath).Should().BeTrue();

            // Read and inspect ndjson last line
            string[] lines = [.. File.ReadAllLines(ndjsonPath).Where(l => !string.IsNullOrWhiteSpace(l) && l.TrimStart().StartsWith('{') && l.TrimEnd().EndsWith('}'))];
            if (lines.Length == 0) {
                string[] allLines = File.ReadAllLines(ndjsonPath);
            }
            lines.Should().NotBeNullOrEmpty();
            string last = lines.Last();

            using var doc = JsonDocument.Parse(last);
            JsonElement root = doc.RootElement;
            root.GetProperty("StatusCode").GetInt32().Should().Be(201);

            // CSV header contains the StatusCode column
            string csvAll = File.ReadAllText(csvPath);
            csvAll.Should().Contain("StatusCode");
        }
        finally {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
