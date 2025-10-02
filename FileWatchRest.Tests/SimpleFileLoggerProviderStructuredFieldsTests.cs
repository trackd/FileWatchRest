namespace FileWatchRest.Tests;

public class SimpleFileLoggerProviderStructuredFieldsTests
{
    [Fact]
    public void Logger_Emits_StatusCode_In_Json_And_Csv()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "FileWatchRest_TestLogs", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var basePath = Path.Combine(tempDir, "testlog");

        try
        {
            var options = new SimpleFileLoggerOptions
            {
                LogType = LogType.Both,
                FilePathPattern = basePath,
                RetainedFileCountLimit = 10
            };

            var provider = new SimpleFileLoggerProvider(options);
            var logger = provider.CreateLogger("UnitTest");

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Upload result for {Path} with StatusCode {StatusCode}",
                    "file.txt",
                    201);
            }

            provider.Dispose();

            var ndjsonPath = basePath + ".json";
            var csvPath = basePath + ".csv";

            File.Exists(ndjsonPath).Should().BeTrue();
            File.Exists(csvPath).Should().BeTrue();

            // Read and inspect ndjson last line
            var lines = File.ReadAllLines(ndjsonPath).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            lines.Should().NotBeNullOrEmpty();
            var last = lines.Last();

            using var doc = JsonDocument.Parse(last);
            var root = doc.RootElement;
            root.GetProperty("StatusCode").GetInt32().Should().Be(201);

            // CSV header contains the StatusCode column
            var csvAll = File.ReadAllText(csvPath);
            csvAll.Should().Contain("StatusCode");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
