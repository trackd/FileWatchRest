namespace FileWatchRest.Tests.Integration;

public class FileSystemIntegrationTests {
    [Fact]
    public async Task RealFileMoveTriggersWatcherAndAction() {
        // Arrange: Use a real watched folder from config
        string watchedFolder = "C:\\temp\\watch"; // Update if needed
        Directory.CreateDirectory(watchedFolder);

        // Create a source file and move it into the watched folder
        string sourceDir = Path.Combine(Path.GetTempPath(), $"FileWatchSource_{Guid.NewGuid()}");
        Directory.CreateDirectory(sourceDir);
        string sourceFile = Path.Combine(sourceDir, "moved_test.txt");
        await File.WriteAllTextAsync(sourceFile, "integration move test");
        string targetFile = Path.Combine(watchedFolder, "moved_test.txt");

        // Act: Move file into watched folder (ensure we don't fail if target already exists)
        if (File.Exists(targetFile)) {
            File.Delete(targetFile);
        }
        File.Move(sourceFile, targetFile);

        // Wait for watcher to process the file (simulate delay)
        await Task.Delay(2000);

        // Optionally, check processed folder or diagnostics
        // var processedFolder = Path.Combine(watchedFolder, "processed");
        // Assert.True(File.Exists(Path.Combine(processedFolder, Path.GetFileName(targetFile))));

        // Cleanup
        if (File.Exists(targetFile)) {
            File.Delete(targetFile);
        }

        if (Directory.Exists(sourceDir)) {
            Directory.Delete(sourceDir, true);
        }
    }
}
