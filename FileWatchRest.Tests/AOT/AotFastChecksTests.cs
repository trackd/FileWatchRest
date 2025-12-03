namespace FileWatchRest.Tests;

public class AotFastChecksTests {
    [Fact]
    public void JsonSourceGen_IncludesPublicModels_And_LogContext() {
        MyJsonContext ctx = MyJsonContext.Default;
        Type ctxType = typeof(MyJsonContext);

        MyJsonLogContext logCtx = MyJsonLogContext.Default;
        Type logCtxType = typeof(MyJsonLogContext);

        string[] expected = [
            "ExternalConfiguration",
            "ActionConfig",
            "WatchedFolderConfig",
            "SimpleFileLoggerOptions",
            "LogWriteEntry",
            "FileEventRecord",
            "DiagnosticStatus",
            "HealthStatus"
        ];

        foreach (string name in expected) {
            PropertyInfo? prop = ctxType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(prop);
            object? val = prop.GetValue(ctx);
            Assert.NotNull(val);
        }

        // Ensure the compact log context also exposes LogWriteEntry
        PropertyInfo? logProp = logCtxType.GetProperty("LogWriteEntry", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(logProp);
        object? logVal = logProp.GetValue(logCtx);
        Assert.NotNull(logVal);
    }

    [Fact]
    public void NoNonGenericJsonStringEnumConverterAttributes() {
        // Ensure code uses the generic JsonStringEnumConverter<T> where applied
        Assembly asm = typeof(MyJsonContext).Assembly;
        foreach (Type t in asm.GetTypes()) {
            foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)) {
                foreach (CustomAttributeData cad in p.CustomAttributes) {
                    if (cad.AttributeType.FullName == "System.Text.Json.Serialization.JsonConverterAttribute") {
                        if (cad.ConstructorArguments.Count > 0) {
                            var arg = cad.ConstructorArguments[0].Value as Type;
                            if (arg is not null) {
                                // Disallow plain JsonStringEnumConverter (non-generic)
                                Assert.False(arg.Name == "JsonStringEnumConverter", $"Non-generic JsonStringEnumConverter used on {t.FullName}.{p.Name}");
                            }
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void SourceScan_NoObviousStringTypeResolutionCalls() {
        // Quick static scan for literal string usages that indicate runtime Type.GetType("...") or Activator.CreateInstance("...")
        // Find repo root by searching for the solution file upward from the test assembly location
        string repoRoot = AppContext.BaseDirectory;
        string? current = repoRoot;
        while (current is not null && !File.Exists(Path.Combine(current, "FileWatchRest.sln"))) {
            DirectoryInfo? parent = Directory.GetParent(current);
            current = parent?.FullName;
        }
        repoRoot = current is not null ? current : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string[] patterns = [
            "Type.GetType(\"",
            "Activator.CreateInstance(\"",
            "Assembly.Load(\""
        ];

        var matches = new List<string>();
        // Limit scan to the main project source folder to avoid scanning unrelated sibling repos in the same parent
        string srcDir = Path.Combine(repoRoot, "FileWatchRest");
        if (!Directory.Exists(srcDir)) srcDir = repoRoot;
        foreach (string file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories)) {
            if (file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) || file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
            string text = File.ReadAllText(file);
            foreach (string p in patterns) {
                if (text.Contains(p, StringComparison.Ordinal)) {
                    matches.Add(file + ":" + p);
                }
            }
        }

        // Fail test if any matches found; this is a conservative heuristic only.
        Assert.Empty(matches);
    }
}
