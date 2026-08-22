using System.Text.Json.Serialization;

namespace TaikoDiveLauncher.Models;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(LauncherPreferences))]
internal partial class LauncherJsonContext : JsonSerializerContext;
