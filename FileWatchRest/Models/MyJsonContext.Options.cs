namespace FileWatchRest.Models;

internal partial class MyJsonContext {
    /// <summary>
    /// Cached options for saving configuration in a compact, user-friendly form.
    /// Use WhenWritingNull to avoid persisting null-valued properties that clutter user files.
    /// </summary>
    internal static readonly JsonSerializerOptions SaveOptions = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
}
