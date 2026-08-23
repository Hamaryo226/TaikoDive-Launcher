using System.Text.Json.Serialization;

namespace TaikoDiveLauncher.Models;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(LauncherPreferences))]
[JsonSerializable(typeof(LauncherUpdateManifest))]
[JsonSerializable(typeof(GameUpdateManifest))]
[JsonSerializable(typeof(GamePackageManifest))]
[JsonSerializable(typeof(InstalledGameUpdate))]
internal partial class LauncherJsonContext : JsonSerializerContext;
