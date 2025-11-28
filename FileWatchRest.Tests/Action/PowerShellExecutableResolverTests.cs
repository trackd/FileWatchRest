
namespace FileWatchRest.Tests.Action;

public class PowerShellExecutableResolverTests {
    [Fact]
    public async Task ExecuteAsync_RunsWithPowershellExe_WhenResolverProvidesIt() {
        var fileEvent = new FileEventRecord("C:\\temp\\file.txt", DateTimeOffset.UtcNow, false, null);
        string scriptPath = Path.GetTempFileName() + ".ps1";
        string outputPath = Path.GetTempFileName() + ".out";
        try {
            File.WriteAllText(scriptPath, $"$PSVersionTable.PSVersion.ToString() | Out-File -FilePath \"{outputPath}\" -Encoding utf8");

            static string? resolver(string name) {
                return name == "pwsh" ? null : (name == "powershell.exe" ? "powershell.exe" : null);
            }

            var action = new PowerShellScriptAction(scriptPath, null, null, null, false, resolver);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await action.ExecuteAsync(fileEvent, cts.Token);

            File.Exists(outputPath).Should().BeTrue();
            string versionText = File.ReadAllText(outputPath).Trim();
            Version.TryParse(versionText, out Version? ver).Should().BeTrue();
            ver!.Major.Should().Be(5);
            ver.Minor.Should().Be(1);
        }
        finally { try { File.Delete(scriptPath); } catch { } try { File.Delete(outputPath); } catch { } }
    }

    [Fact]
    public void CreateProcessStartInfo_UsesInjectedResolverWhenProvided() {
        var fileEvent = new FileEventRecord("C:\\temp\\file.txt", DateTimeOffset.UtcNow, false, null);
        string scriptPath = Path.GetTempFileName() + ".ps1";
        try {
            File.WriteAllText(scriptPath, "Write-Output 'hi'");

            static string? resolver(string name) {
                return name == "pwsh" ? "C:\\custom\\pwsh.exe" : null;
            }

            var action = new PowerShellScriptAction(scriptPath, null, null, null, false, resolver);
            ProcessStartInfo psi = action.CreateProcessStartInfo(fileEvent);
            psi.FileName.Should().Be("C:\\custom\\pwsh.exe");
        }
        finally { try { File.Delete(scriptPath); } catch { } }
    }

}
