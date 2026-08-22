using System.Text.Json.Serialization;

namespace TaikoDiveLauncher.Models;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(LauncherPreferences))]
[JsonSerializable(typeof(LauncherUpdateManifest))]
internal partial class LauncherJsonContext : JsonSerializerContext;
