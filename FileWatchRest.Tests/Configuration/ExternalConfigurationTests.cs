namespace FileWatchRest.Tests.Configuration;

public class ExternalConfigurationTests {
    [Fact]
    public void ValidateFolders_reports_missing_fields_and_unknown_action() {
        var cfg = new ExternalConfiguration();
        cfg.Folders.Add(new ExternalConfiguration.WatchedFolderConfig { FolderPath = "", ActionName = "" });
        cfg.Folders.Add(new ExternalConfiguration.WatchedFolderConfig { FolderPath = "C:\\x", ActionName = "missing" });

        var errors = cfg.ValidateFolders().ToList();

        Assert.Single(errors, e => e.Contains("FolderPath is required"));
        Assert.Contains(errors, e => e.Contains("ActionName is required") || e.Contains("references action 'missing'"));
    }

    [Fact]
    public void ValidateActions_reports_missing_action_and_duplicates_and_missing_required_properties() {
        var cfg = new ExternalConfiguration();

        // No actions defined
        var errs = cfg.ValidateActions().ToList();
        var singleErr = Assert.Single(errs);
        Assert.Contains("At least one Action", singleErr);

        cfg.Actions.Add(new ExternalConfiguration.ActionConfig { Name = "", ActionType = ExternalConfiguration.FolderActionType.RestPost });
        cfg.Actions.Add(new ExternalConfiguration.ActionConfig { Name = "A1", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "" });
        cfg.Actions.Add(new ExternalConfiguration.ActionConfig { Name = "A1", ActionType = ExternalConfiguration.FolderActionType.PowerShellScript, ScriptPath = "" });

        var errs2 = cfg.ValidateActions().ToList();
        Assert.Contains(errs2, e => e.Contains("Action Name is required"));
        Assert.Contains(errs2, e => e.Contains("Duplicate action name 'A1'"));
        Assert.Contains(errs2, e => e.Contains("ApiEndpoint is required for REST action 'A1'"));
        Assert.Contains(errs2, e => e.Contains("ScriptPath is required for PowerShell action 'A1'"));
    }

    [Fact]
    public void WatchedFolderConfig_equals_and_hashcode_case_insensitive() {
        var a = new ExternalConfiguration.WatchedFolderConfig { FolderPath = "C:\\Temp", ActionName = "DoWork" };
        var b = new ExternalConfiguration.WatchedFolderConfig { FolderPath = "c:\\temp", ActionName = "dowork" };

        Assert.True(a.Equals(b));
        Assert.Equal(b.GetHashCode(), a.GetHashCode());
    }
}
