namespace FileWatchRest.Tests;

public class ProgramHelpersTests {
    [Fact]
    public void ReturnsConfigFromLongArg() {
        string[] args = ["--config", "C:\\tmp\\mycfg.json"];
        string path = ProgramHelpers.GetExternalConfigPath(args, envGetter: () => null, programData: "C:\\pd", existsChecker: _ => false);
        Assert.Equal("C:\\tmp\\mycfg.json", path);
    }

    [Fact]
    public void ReturnsConfigFromShortArg() {
        string[] args = ["-c", "C:\\tmp\\short.json"];
        string path = ProgramHelpers.GetExternalConfigPath(args, envGetter: () => null, programData: "C:\\pd", existsChecker: _ => false);
        Assert.Equal("C:\\tmp\\short.json", path);
    }

    [Fact]
    public void ReturnsPositionalIfFileExists() {
        string[] args = ["C:\\exists.json"];
        string path = ProgramHelpers.GetExternalConfigPath(args, envGetter: () => null, programData: "C:\\pd", existsChecker: p => p == "C:\\exists.json");
        Assert.Equal("C:\\exists.json", path);
    }

    [Fact]
    public void ReturnsEnvWhenNoArgs() {
        string[] args = [];
        string path = ProgramHelpers.GetExternalConfigPath(args, envGetter: () => "D:\\env.json", programData: "C:\\pd", existsChecker: _ => false);
        Assert.Equal("D:\\env.json", path);
    }

    [Fact]
    public void ReturnsDefaultWhenNothingElse() {
        string[]? args = null;
        string path = ProgramHelpers.GetExternalConfigPath(args, envGetter: () => null, programData: "C:\\pd", existsChecker: _ => false);
        Assert.Equal(Path.Combine("C:\\pd", "FileWatchRest", "FileWatchRest.json"), path);
    }
}
