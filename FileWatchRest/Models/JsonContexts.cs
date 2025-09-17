using System.Text.Json.Serialization;
using FileWatchRest.Configuration;

namespace FileWatchRest.Models;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(FileNotification))]
[JsonSerializable(typeof(ExternalConfiguration))]
[JsonSerializable(typeof(object))] // fallback for diagnostics anonymous objects if needed
internal partial class MyJsonContext : JsonSerializerContext
{
}
