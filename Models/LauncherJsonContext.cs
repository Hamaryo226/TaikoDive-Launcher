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
[JsonSerializable(typeof(OutfitCollection))]
[JsonSerializable(typeof(DemoSongSettings))]
[JsonSerializable(typeof(UpdateHistory))]
[JsonSerializable(typeof(LauncherRollbackEntry))]
internal partial class LauncherJsonContext : JsonSerializerContext;
