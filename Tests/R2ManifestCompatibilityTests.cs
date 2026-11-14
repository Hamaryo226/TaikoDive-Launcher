using System.Text.Json;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Tests;

[TestClass]
public sealed class R2ManifestCompatibilityTests
{
    // Characterization: existing clients must ignore storage-only retention metadata.
    // No production model or updater behavior is changed by these tests.
    [TestMethod]
    [DataRow("game", "Update")]
    [DataRow("assets", "Assets")]
    public void StorageHistoryMetadataDoesNotChangeClientValidation(string channel, string kind)
    {
        string revision = "0123456789abcdef0123456789abcdef01234567";
        string packageName = $"TaikoDive_{kind}_v1.2.3_win-x64_0123456.zip";
        string json = $$"""
            {
              "version": "1.2.3",
              "revision": "{{revision}}",
              "releaseNotes": "Compatibility fixture",
              "packageFileName": "{{packageName}}",
              "packageUrl": "https://dl.hamaryo.dev/{{channel}}/{{packageName}}",
              "sha256": "{{new string('A', 64)}}",
              "size": 123,
              "publishedAt": "2026-01-01T00:00:00Z",
              "archive": { "format": "zip", "encryption": "winzip-aes-256", "payload": "payload.bin", "keyId": "2026-01" },
              "_r2PreviousPackageKey": "{{channel}}/TaikoDive_{{kind}}_v1.2.2_win-x64_abcdef0.zip"
            }
            """;

        GameUpdateManifest? manifest = JsonSerializer.Deserialize(json, LauncherJsonContext.Default.GameUpdateManifest);
        Assert.IsNotNull(manifest);
        Assert.AreEqual(packageName, manifest.PackageFileName);
        Assert.AreEqual(123L, manifest.Size);
        if (channel == "game")
            GameUpdateService.ValidateManifest(manifest);
        else
            GameUpdateService.ValidateAssetManifest(manifest);
    }
}
