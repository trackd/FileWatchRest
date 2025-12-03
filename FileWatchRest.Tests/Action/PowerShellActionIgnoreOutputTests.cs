
namespace FileWatchRest.Tests.Action;

public class PowerShellActionIgnoreOutputTests {
    [Fact]
    public void CreateProcessStartInfo_RespectsIgnoreOutputFlag() {
        var fileEvent = new FileEventRecord("C:\\temp\\file.txt", DateTimeOffset.UtcNow, false, null);

        // Create a temporary PowerShell script for the test so we run against a real file
        string scriptPath = Path.GetTempFileName() + ".ps1";
        try {
            File.WriteAllText(scriptPath, "param($File); if ($File) { Write-Output $File }\n");

            var args = new List<string> { "-File", "{FilePath}" };

            var actionWithOutput = new PowerShellScriptAction(scriptPath, args, executionTimeoutMilliseconds: 1000, ignoreOutput: false);
            ProcessStartInfo psiWith = actionWithOutput.CreateProcessStartInfo(fileEvent);
            psiWith.RedirectStandardOutput.Should().BeTrue();
            psiWith.RedirectStandardError.Should().BeTrue();

            var actionIgnore = new PowerShellScriptAction(scriptPath, args, executionTimeoutMilliseconds: 1000, ignoreOutput: true);
            ProcessStartInfo psiIgnore = actionIgnore.CreateProcessStartInfo(fileEvent);
            psiIgnore.RedirectStandardOutput.Should().BeFalse();
            psiIgnore.RedirectStandardError.Should().BeFalse();
        }
        finally {
            try { File.Delete(scriptPath); } catch { }
        }
    }
}
