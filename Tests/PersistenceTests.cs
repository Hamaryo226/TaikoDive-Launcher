using System.Text;
using System.Text.Json.Nodes;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Tests;

[TestClass]
public sealed class PersistenceTests
{
    [TestMethod]
    public async Task UserProfileSavePreservesCommentsUnknownKeysEncodingAndNewlines()
    {
        using TemporaryInstallation temporary = new();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding shiftJis = Encoding.GetEncoding(932);
        string original =
            "; launcher must keep this comment\r\n" +
            "Unknown_Key=keep-me\r\n" +
            "1P_User_Name=old\r\n" +
            "1P_User_Title=old-title\r\n" +
            "1P_User_NamePlateType=2\r\n" +
            "1P_User_CharaType=1\r\n";
        await File.WriteAllTextAsync(temporary.Installation.UserProfilePath, original, shiftJis);

        UserProfileStore store = new();
        IReadOnlyList<UserProfile> loaded = await store.LoadAsync(temporary.Installation);
        Assert.AreEqual("old", loaded[0].Name);

        await store.SaveAsync(temporary.Installation, new UserProfile
        {
            Slot = 1,
            Name = "どんちゃん",
            Title = "新しい称号",
            NamePlateType = 43,
            CharaType = "4",
            IsConfigured = true,
        });

        byte[] savedBytes = await File.ReadAllBytesAsync(temporary.Installation.UserProfilePath);
        string saved = shiftJis.GetString(savedBytes);
        StringAssert.Contains(saved, "; launcher must keep this comment\r\n");
        StringAssert.Contains(saved, "Unknown_Key=keep-me\r\n");
        StringAssert.Contains(saved, "1P_User_Name=どんちゃん\r\n");
        StringAssert.Contains(saved, "1P_User_Title=新しい称号\r\n");
        StringAssert.Contains(saved, "1P_User_NamePlateType=43\r\n");
        StringAssert.Contains(saved, "1P_User_CharaType=4\r\n");
        Assert.IsTrue(File.Exists(temporary.Installation.UserProfilePath + ".launcher.bak"));
        Assert.AreEqual(original, await File.ReadAllTextAsync(
            temporary.Installation.UserProfilePath + ".launcher.bak",
            shiftJis));
    }

    [TestMethod]
    public async Task GameSettingsSavePreservesUnknownPropertiesAndCreatesBackup()
    {
        using TemporaryInstallation temporary = new();
        string original = """
            {
              "guestMode": true,
              "screenWidth": 1280,
              "masterVolume": 90,
              "futureSetting": {
                "enabled": true
              }
            }
            """;
        await File.WriteAllTextAsync(temporary.Installation.GameSettingsPath, original);

        GameSettingsStore store = new();
        GameSettings settings = await store.LoadAsync(temporary.Installation);
        settings.GuestMode = false;
        settings.ScreenWidth = 1920;
        settings.MasterVolume = 64;
        await store.SaveAsync(temporary.Installation, settings);

        JsonObject saved = JsonNode.Parse(
            await File.ReadAllTextAsync(temporary.Installation.GameSettingsPath))!.AsObject();
        Assert.IsFalse(saved["guestMode"]!.GetValue<bool>());
        Assert.AreEqual(1920, saved["screenWidth"]!.GetValue<int>());
        Assert.AreEqual(64, saved["masterVolume"]!.GetValue<int>());
        Assert.IsTrue(saved["futureSetting"]!["enabled"]!.GetValue<bool>());
        Assert.IsTrue(File.Exists(temporary.Installation.GameSettingsPath + ".launcher.bak"));
    }

    [TestMethod]
    public void InstallationAcceptsRepositoryRootOrBuildDirectory()
    {
        using TemporaryInstallation temporary = new();

        TaikoDiveInstallation? fromRoot = TaikoDiveInstallation.FromSelectedDirectory(temporary.RootDirectory);
        TaikoDiveInstallation? fromBuild = TaikoDiveInstallation.FromSelectedDirectory(temporary.Installation.BuildDirectory);

        Assert.IsNotNull(fromRoot);
        Assert.IsNotNull(fromBuild);
        Assert.AreEqual(temporary.Installation.BuildDirectory, fromRoot.BuildDirectory);
        Assert.AreEqual(temporary.Installation.BuildDirectory, fromBuild.BuildDirectory);
    }

    private sealed class TemporaryInstallation : IDisposable
    {
        public TemporaryInstallation()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "TaikoDiveLauncher.Tests", Guid.NewGuid().ToString("N"));
            string buildDirectory = Path.Combine(RootDirectory, "build");
            Directory.CreateDirectory(Path.Combine(buildDirectory, "Info"));
            File.WriteAllBytes(Path.Combine(buildDirectory, "TaikoDive.exe"), [0]);
            Installation = new TaikoDiveInstallation(buildDirectory);
        }

        public string RootDirectory { get; }

        public TaikoDiveInstallation Installation { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootDirectory))
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
        }
    }
}
