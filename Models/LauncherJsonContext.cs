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
[JsonSerializable(typeof(IntegrationSettings))]
[JsonSerializable(typeof(PlayHistoryEntry))]
[JsonSerializable(typeof(UpdateHistory))]
[JsonSerializable(typeof(LauncherRollbackEntry))]
internal partial class LauncherJsonContext : JsonSerializerContext;
