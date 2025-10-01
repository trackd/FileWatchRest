namespace FileWatchRest.Tests;

public class SimpleFileLoggerProviderStatusCodeTests
{
    [Fact]
    public void Logger_Captures_StatusCode_And_CsvContains_StatusColumn()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "FileWatchRest_TestLogs", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var csvPath = Path.Combine(tempDir, "test.csv");

        try
        {
            var options = new SimpleFileLoggerOptions
            {
                LogType = LogType.Csv,
                FilePathPattern = csvPath, // allow full path (with extension)
                RetainedFileCountLimit = 10
            };

            var provider = new SimpleFileLoggerProvider(options);
            var logger = provider.CreateLogger("UnitTest");

            // Log a structured message that includes StatusCode
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Upload result for {Path} with StatusCode {StatusCode}", "file.txt", 201);
            }

            // Ensure the provider flushes and closes the writer so the test can read the file
            provider.Dispose();

            // Ensure file exists and read contents
            var allText = File.ReadAllText(csvPath);

            // Header should include StatusCode
            allText.Should().Contain("StatusCode");
            // The logged line should contain the status code 201
            allText.Should().Contain(",201");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
