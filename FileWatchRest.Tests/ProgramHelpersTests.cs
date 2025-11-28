namespace FileWatchRest.Tests;

public class ProgramHelpersTests {
    [Fact]
    public void ReturnsConfigFromLongArg() {
        string[] args = ["--config", "C:\\tmp\\mycfg.json"];
        string path = ProgramHelpers.GetExternalConfigPath(args, envGetter: () => null, programData: "C:\\pd", existsChecker: _ => false);
        path.Should().Be("C:\\tmp\\mycfg.json");
    }

    [Fact]
    public void ReturnsConfigFromShortArg() {
        string[] args = ["-c", "C:\\tmp\\short.json"];
        string path = ProgramHelpers.GetExternalConfigPath(args, envGetter: () => null, programData: "C:\\pd", existsChecker: _ => false);
        path.Should().Be("C:\\tmp\\short.json");
    }

    [Fact]
    public void ReturnsPositionalIfFileExists() {
        string[] args = ["C:\\exists.json"];
        string path = ProgramHelpers.GetExternalConfigPath(args, envGetter: () => null, programData: "C:\\pd", existsChecker: p => p == "C:\\exists.json");
        path.Should().Be("C:\\exists.json");
    }

    [Fact]
    public void ReturnsEnvWhenNoArgs() {
        string[] args = [];
        string path = ProgramHelpers.GetExternalConfigPath(args, envGetter: () => "D:\\env.json", programData: "C:\\pd", existsChecker: _ => false);
        path.Should().Be("D:\\env.json");
    }

    [Fact]
    public void ReturnsDefaultWhenNothingElse() {
        string[]? args = null;
        string path = ProgramHelpers.GetExternalConfigPath(args, envGetter: () => null, programData: "C:\\pd", existsChecker: _ => false);
        path.Should().Be(Path.Combine("C:\\pd", "FileWatchRest", "FileWatchRest.json"));
    }
}
