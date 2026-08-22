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

    [TestMethod]
    public void CharacterPreviewUsesConfiguredNormalLoopAndCapsDecodedFrames()
    {
        using TemporaryInstallation temporary = new();
        string characterRoot = Path.Combine(temporary.Installation.CharacterDirectory, "4");
        string normalRoot = Path.Combine(characterRoot, "result_loop");
        Directory.CreateDirectory(normalRoot);
        File.WriteAllText(Path.Combine(temporary.Installation.BuildDirectory, "Info", "CharaPath.ini"),
            "Chara_Root=Info\\Chara\\<Type>\nCommon_NormalLoop=result_loop|Common\\Normal_loop\n");
        File.WriteAllText(Path.Combine(characterRoot, "Config.json"), "{ \"resultLoopTime\": 600 }");
        for (int index = 0; index < 40; index++)
        {
            File.WriteAllBytes(Path.Combine(normalRoot, $"{index}.png"), [0]);
        }

        CharacterPreviewData? preview = CharacterPreviewService.Load(temporary.Installation, "4");

        Assert.IsNotNull(preview);
        Assert.HasCount(30, preview.Frames);
        Assert.AreEqual(20, preview.FrameInterval.TotalMilliseconds);
    }

    [TestMethod]
    public void Aup2AnimationLoadsSceneFramesAndAnimatedValues()
    {
        string root = Path.Combine(Path.GetTempPath(), "TaikoDiveLauncher.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "Plate.png"), [0]);
            string path = Path.Combine(root, "Anime.aup2");
            File.WriteAllText(path, """
                [scene.0]
                video.width=380
                video.height=100
                video.rate=60
                [0]
                layer=2
                frame=0,29
                [0.0]
                effect.name=画像ファイル
                ファイル=\Plate.png
                [0.1]
                effect.name=標準描画
                X=-100.00,100.00,直線移動,0
                Y=0.00
                拡大率=100.00
                透明度=0.00,100.00,直線移動,0
                """);

            Aup2Animation? animation = Aup2Animation.Load(path);

            Assert.IsNotNull(animation);
            Assert.AreEqual(380, animation.Width);
            Assert.AreEqual(100, animation.Height);
            Assert.AreEqual(30, animation.TotalFrames);
            Assert.HasCount(1, animation.Visuals);
            Assert.AreEqual(0, animation.Visuals[0].X.At(0.5), 0.001);
            Assert.AreEqual(50, animation.Visuals[0].Transparency.At(0.5), 0.001);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
