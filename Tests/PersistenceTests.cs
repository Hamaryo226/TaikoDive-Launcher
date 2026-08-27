using System.IO.Compression;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;
using Windows.System;

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

        UserProfileStore store = new(() => false);
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
    public async Task UserProfileTitleCanBeEnteredAfterSavingItEmpty()
    {
        using TemporaryInstallation temporary = new();
        const string original =
            "; keep this comment\r\n" +
            "1P_User_Name=どんちゃん\r\n" +
            "1P_User_Title=古い称号\r\n" +
            "Unknown_Key=keep-me\r\n" +
            "1P_User_Title=重複した古い称号\r\n" +
            "1P_User_NamePlateType=0\r\n" +
            "1P_User_CharaType=0\r\n";
        await File.WriteAllTextAsync(temporary.Installation.UserProfilePath, original, Encoding.UTF8);
        UserProfileStore store = new(() => false);

        await store.SaveAsync(temporary.Installation, new UserProfile
        {
            Slot = 1,
            Name = "どんちゃん",
            Title = string.Empty,
            NamePlateType = 0,
            CharaType = "0",
            IsConfigured = true,
        });
        await store.SaveAsync(temporary.Installation, new UserProfile
        {
            Slot = 1,
            Name = "どんちゃん",
            Title = "戻した称号",
            NamePlateType = 0,
            CharaType = "0",
            IsConfigured = true,
        });

        string saved = await File.ReadAllTextAsync(temporary.Installation.UserProfilePath, Encoding.UTF8);
        IReadOnlyList<UserProfile> loaded = await store.LoadAsync(temporary.Installation);
        Assert.AreEqual("戻した称号", loaded[0].Title);
        Assert.HasCount(1, Regex.Matches(saved, "^1P_User_Title=", RegexOptions.Multiline).Cast<Match>());
        StringAssert.Contains(saved, "; keep this comment\r\n");
        StringAssert.Contains(saved, "Unknown_Key=keep-me\r\n");
    }

    [TestMethod]
    public void KeyboardKeyConversionFallsBackWhenWinUiOmitsTheScanCode()
    {
        Assert.AreEqual(30, InputLabelService.ToDxLibKeyCode(VirtualKey.A, 0, isExtendedKey: false));
        Assert.AreEqual(200, InputLabelService.ToDxLibKeyCode(VirtualKey.Up, 0, isExtendedKey: false));
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

        GameSettingsStore store = new(() => false);
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
    public async Task InputBindingsSavePreservesUnknownPropertiesAndRoundTripsControllerInput()
    {
        using TemporaryInstallation temporary = new();
        string original = """
            {
              "futureSetting": true,
              "p1Keys": {
                "kaLeft": [32],
                "futureBinding": "keep"
              }
            }
            """;
        await File.WriteAllTextAsync(temporary.Installation.GameSettingsPath, original);

        InputBindingsStore store = new(() => false);
        InputBindings bindings = await store.LoadAsync(temporary.Installation);
        bindings.Player1.DonLeftControllers =
        [
            new ControllerInputBinding
            {
                VendorId = 0x1234,
                ProductId = 0x5678,
                DeviceIndex = 1,
                DeviceOrdinal = 0,
                DeviceName = "Taiko Controller",
                InputType = ControllerInputType.Button,
                InputIndex = 7,
            },
        ];
        await store.SaveAsync(temporary.Installation, bindings);

        JsonObject saved = JsonNode.Parse(await File.ReadAllTextAsync(temporary.Installation.GameSettingsPath))!.AsObject();
        Assert.IsTrue(saved["futureSetting"]!.GetValue<bool>());
        Assert.AreEqual("keep", saved["p1Keys"]!["futureBinding"]!.GetValue<string>());
        Assert.AreEqual(7, saved["p1Keys"]!["donLeftControllers"]![0]!["inputIndex"]!.GetValue<int>());

        InputBindings reloaded = await store.LoadAsync(temporary.Installation);
        Assert.HasCount(1, reloaded.Player1.DonLeftControllers);
        Assert.AreEqual((ushort)0x1234, reloaded.Player1.DonLeftControllers[0].VendorId);
        Assert.AreEqual("Taiko Controller", reloaded.Player1.DonLeftControllers[0].DeviceName);
    }

    [TestMethod]
    public void InputBindingsEditorRemovesOnlyTheSelectedBindingTypeAndItem()
    {
        ControllerInputBinding firstController = new()
        {
            VendorId = 0x1234,
            ProductId = 0x5678,
            DeviceOrdinal = 0,
            DeviceName = "Taiko Controller",
            InputType = ControllerInputType.Button,
            InputIndex = 1,
        };
        ControllerInputBinding secondController = new()
        {
            VendorId = 0x1234,
            ProductId = 0x5678,
            DeviceOrdinal = 0,
            DeviceName = "Taiko Controller",
            InputType = ControllerInputType.Button,
            InputIndex = 2,
        };
        ControllerInputBinding duplicateFirstController = new()
        {
            VendorId = firstController.VendorId,
            ProductId = firstController.ProductId,
            DeviceOrdinal = firstController.DeviceOrdinal,
            DeviceName = firstController.DeviceName,
            InputType = firstController.InputType,
            InputIndex = firstController.InputIndex,
        };
        PlayerInputBindings player = new()
        {
            DonLeft = [32, 32, 33],
            DonLeftControllers = [firstController, duplicateFirstController, secondController],
        };

        Assert.IsTrue(InputBindingsEditor.RemoveKeyboard(player, 1, 32));
        CollectionAssert.AreEqual(new[] { 32, 33 }, player.DonLeft);
        Assert.HasCount(3, player.DonLeftControllers);

        Assert.IsTrue(InputBindingsEditor.RemoveController(player, 1, firstController));
        CollectionAssert.AreEqual(new[] { 32, 33 }, player.DonLeft);
        Assert.HasCount(2, player.DonLeftControllers);
        Assert.AreEqual(1, player.DonLeftControllers[0].InputIndex);
        Assert.AreEqual(2, player.DonLeftControllers[1].InputIndex);
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
    public void InstallationUsesLauncherExecutableDirectoryForSingleFileBuilds()
    {
        using TemporaryInstallation temporary = new();
        string launcherPath = Path.Combine(temporary.Installation.BuildDirectory, "TaikoDive.Launcher.exe");
        string fallbackDirectory = Path.Combine(temporary.RootDirectory, "extracted-runtime");

        string resolvedDirectory = TaikoDiveInstallation.ResolveApplicationDirectory(launcherPath, fallbackDirectory);
        TaikoDiveInstallation? installation = TaikoDiveInstallation.FromSelectedDirectory(resolvedDirectory);

        Assert.AreEqual(temporary.Installation.BuildDirectory, resolvedDirectory);
        Assert.IsNotNull(installation);
        Assert.AreEqual(temporary.Installation.ExecutablePath, installation.ExecutablePath);
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
                合成モード=加算
                """);

            Aup2Animation? animation = Aup2Animation.Load(path);

            Assert.IsNotNull(animation);
            Assert.AreEqual(380, animation.Width);
            Assert.AreEqual(100, animation.Height);
            Assert.AreEqual(30, animation.TotalFrames);
            Assert.HasCount(1, animation.Visuals);
            Assert.AreEqual(0, animation.Visuals[0].X.At(0.5), 0.001);
            Assert.AreEqual(50, animation.Visuals[0].Transparency.At(0.5), 0.001);
            Assert.AreEqual(Aup2BlendMode.Additive, animation.Visuals[0].BlendMode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void AdditiveImageConversionMakesBlackTransparentAndPreservesGlowIntensity()
    {
        byte[] pixels =
        [
            0, 0, 0, 255,
            32, 64, 128, 255,
            100, 50, 200, 128,
        ];

        Aup2ImageProcessor.ConvertToPremultipliedBgra(pixels, removeBlackBackground: true);

        CollectionAssert.AreEqual(
            new byte[]
            {
                0, 0, 0, 0,
                32, 64, 128, 128,
                50, 25, 100, 100,
            },
            pixels);
    }

    [TestMethod]
    public async Task SongImportLoadsGenresAndExtractsSingleSongFolder()
    {
        using TemporaryInstallation temporary = new();
        string popGenre = Path.Combine(temporary.Installation.SongsDirectory, "00 ポップス");
        string animeGenre = Path.Combine(temporary.Installation.SongsDirectory, "01 アニメ");
        Directory.CreateDirectory(popGenre);
        Directory.CreateDirectory(animeGenre);
        string zipPath = Path.Combine(temporary.RootDirectory, "Song.zip");
        CreateZip(zipPath,
            ("My Song/chart.tja", "TITLE:My Song"),
            ("My Song/song.ogg", "audio"));

        IReadOnlyList<SongGenre> genres = SongImportService.LoadGenres(temporary.Installation);
        SongImportResult result = await SongImportService.ImportAsync(temporary.Installation, zipPath, genres[0]);

        Assert.HasCount(2, genres);
        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.IsNotNull(result.DestinationPath);
        Assert.AreEqual("My Song", Path.GetFileName(result.DestinationPath));
        Assert.IsTrue(File.Exists(Path.Combine(result.DestinationPath, "chart.tja")));
        Assert.IsTrue(File.Exists(Path.Combine(result.DestinationPath, "song.ogg")));
    }

    [TestMethod]
    public async Task SongImportRejectsPathTraversalAndCleansTemporaryFiles()
    {
        using TemporaryInstallation temporary = new();
        string genrePath = Path.Combine(temporary.Installation.SongsDirectory, "00 ポップス");
        Directory.CreateDirectory(genrePath);
        string zipPath = Path.Combine(temporary.RootDirectory, "Unsafe.zip");
        CreateZip(zipPath,
            ("../outside.tja", "TITLE:Unsafe"),
            ("song.ogg", "audio"));
        SongGenre genre = SongImportService.LoadGenres(temporary.Installation).Single();

        SongImportResult result = await SongImportService.ImportAsync(temporary.Installation, zipPath, genre);

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(File.Exists(Path.Combine(temporary.RootDirectory, "outside.tja")));
        Assert.IsEmpty(Directory.EnumerateDirectories(genrePath, ".launcher-import-*"));
    }

    [TestMethod]
    public async Task SongImportDoesNotOverwriteExistingSongFolder()
    {
        using TemporaryInstallation temporary = new();
        string genrePath = Path.Combine(temporary.Installation.SongsDirectory, "00 ポップス");
        Directory.CreateDirectory(Path.Combine(genrePath, "Existing"));
        string zipPath = Path.Combine(temporary.RootDirectory, "Existing.zip");
        CreateZip(zipPath, ("chart.tja", "TITLE:Existing"));
        SongGenre genre = SongImportService.LoadGenres(temporary.Installation).Single();

        SongImportResult result = await SongImportService.ImportAsync(temporary.Installation, zipPath, genre);

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Message, "上書きしません");
    }

    [TestMethod]
    public async Task SongsPathChangePreservesOriginalAndCopiesOnlyMissingAssets()
    {
        using TemporaryInstallation temporary = new();
        string originalGenre = Path.Combine(temporary.Installation.SongsDirectory, "00 ポップス");
        Directory.CreateDirectory(Path.Combine(originalGenre, "Image"));
        await File.WriteAllTextAsync(Path.Combine(originalGenre, "box.def"), "original-box");
        await File.WriteAllTextAsync(Path.Combine(originalGenre, "CenterText.apt"), "center-asset");
        await File.WriteAllTextAsync(Path.Combine(originalGenre, "Image", "Bar.png"), "bar-asset");
        await File.WriteAllTextAsync(Path.Combine(originalGenre, "original.tja"), "TITLE:Original");
        await File.WriteAllTextAsync(Path.Combine(originalGenre, "preview.ogg"), "song-audio");

        string externalSongs = Path.Combine(temporary.RootDirectory, "ExternalSongs");
        string externalGenre = Path.Combine(externalSongs, "00 ポップス");
        Directory.CreateDirectory(externalGenre);
        await File.WriteAllTextAsync(Path.Combine(externalGenre, "box.def"), "external-box");
        await File.WriteAllTextAsync(Path.Combine(externalGenre, "external.tja"), "TITLE:External");

        OperationResult changed = await SongsPathService.ChangeWithoutProcessCheckAsync(temporary.Installation, externalSongs);

        Assert.IsTrue(changed.Succeeded, changed.Message);
        SongsPathState state = SongsPathService.GetState(temporary.Installation);
        Assert.IsTrue(state.IsRedirected);
        Assert.IsTrue(state.CanRestore);
        Assert.AreEqual(Path.GetFullPath(externalSongs), state.EffectivePath);
        Assert.AreEqual("external-box", await File.ReadAllTextAsync(Path.Combine(externalGenre, "box.def")));
        Assert.AreEqual("center-asset", await File.ReadAllTextAsync(Path.Combine(externalGenre, "CenterText.apt")));
        Assert.AreEqual("bar-asset", await File.ReadAllTextAsync(Path.Combine(externalGenre, "Image", "Bar.png")));
        Assert.IsFalse(File.Exists(Path.Combine(externalGenre, "original.tja")));
        Assert.IsFalse(File.Exists(Path.Combine(externalGenre, "preview.ogg")));
        Assert.IsTrue(File.Exists(Path.Combine(temporary.Installation.SongsDirectory, "00 ポップス", "external.tja")));
        Assert.IsFalse(Directory.Exists(Path.Combine(temporary.Installation.BuildDirectory, "Songs.taikodive-launcher-original")));
        Assert.IsTrue(Directory.Exists(Path.Combine(
            temporary.Installation.BuildDirectory,
            "Info",
            "TaikoDiveLauncher",
            "Songs.original")));

        OperationResult restored = await SongsPathService.RestoreWithoutProcessCheckAsync(temporary.Installation);

        Assert.IsTrue(restored.Succeeded, restored.Message);
        SongsPathState restoredState = SongsPathService.GetState(temporary.Installation);
        Assert.IsFalse(restoredState.IsRedirected);
        Assert.IsFalse(restoredState.CanRestore);
        Assert.IsTrue(File.Exists(Path.Combine(originalGenre, "original.tja")));
        Assert.IsTrue(File.Exists(Path.Combine(externalGenre, "external.tja")));
    }

    [TestMethod]
    public void TaikoNautsDiscoveryReturnsOnlyInstallationsWithSongsDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "TaikoDiveLauncher.Tests", Guid.NewGuid().ToString("N"));
        string validDirectory = Path.Combine(root, "Portable", "TaikoNauts");
        string invalidDirectory = Path.Combine(root, "Old");
        Directory.CreateDirectory(Path.Combine(validDirectory, "Songs"));
        Directory.CreateDirectory(invalidDirectory);
        File.WriteAllBytes(Path.Combine(validDirectory, "TaikoNauts.exe"), [0]);
        File.WriteAllBytes(Path.Combine(invalidDirectory, "TaikoNauts.exe"), [0]);
        try
        {
            IReadOnlyList<TaikoNautsInstallation> installations =
                TaikoNautsDiscoveryService.FindInstallations([root]);

            Assert.HasCount(1, installations);
            Assert.AreEqual(
                Path.GetFullPath(Path.Combine(validDirectory, "TaikoNauts.exe")),
                installations[0].ExecutablePath);
            Assert.AreEqual(
                Path.GetFullPath(Path.Combine(validDirectory, "Songs")),
                installations[0].SongsDirectory);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void WindowPlacementIsClampedIntoTheNearestWorkArea()
    {
        WindowPlacementPreferences placement = new()
        {
            X = 5000,
            Y = -2000,
            Width = 1800,
            Height = 1200,
        };

        WindowBounds result = WindowPlacementBounds.ClampToWorkArea(
            placement,
            new WindowBounds(0, 0, 1920, 1080));

        Assert.AreEqual(new WindowBounds(120, 0, 1800, 1080), result);
    }

    [TestMethod]
    public async Task UpdateCheckerDetectsDifferentMainRevisionWithoutAuthentication()
    {
        string currentRevision = new('a', 40);
        string latestRevision = new('b', 40);
        string manifest = $$"""
            {
              "revision": "{{latestRevision}}",
              "releaseNotes": "・設定画面で更新内容を表示",
              "sha256": "{{new string('C', 64)}}",
              "size": 172552040,
              "publishedAt": "2026-08-22T00:00:00Z"
            }
            """;
        using HttpClient client = new(new StaticResponseHandler(manifest));
        LauncherUpdateService service = new(
            client,
            currentRevision,
            new Uri("https://example.test/update-manifest.json"),
            new Uri("https://example.test/TaikoDive.Launcher.exe"),
            () => "C:\\TaikoDive\\TaikoDive.Launcher.exe");

        await service.CheckAsync();

        Assert.AreEqual(LauncherUpdateState.Available, service.State);
        Assert.IsNotNull(service.AvailableUpdate);
        Assert.AreEqual(latestRevision, service.AvailableUpdate.Revision);
        Assert.IsNotNull(service.LatestUpdate);
        Assert.AreEqual("・設定画面で更新内容を表示", service.LatestUpdate.ReleaseNotes);
    }

    [TestMethod]
    public void UpdateApplyCommandRejectsInvalidHashAndAcceptsVerifiedShape()
    {
        string[] validArgs =
        [
            "TaikoDive.Launcher.exe",
            "--apply-update",
            "--target", "C:\\Games\\TaikoDive\\TaikoDive.Launcher.exe",
            "--parent", "1234",
            "--working-directory", "C:\\Games\\TaikoDive",
            "--sha256", new string('A', 64),
        ];
        string[] invalidArgs = [.. validArgs[..^1], "not-a-hash"];

        Assert.IsTrue(LauncherUpdateService.TryParseApplyCommand(validArgs, out PendingUpdateCommand? command));
        Assert.IsNotNull(command);
        Assert.IsFalse(LauncherUpdateService.TryParseApplyCommand(invalidArgs, out _));
    }

    [TestMethod]
    public void GameUpdatePackageNameAndPasswordAreDeterministic()
    {
        string revision = "0123456789abcdef0123456789abcdef01234567";
        string fileName = GameUpdatePackageNaming.Create("1.2.3", revision);

        Assert.AreEqual("TaikoDive_Update_v1.2.3_win-x64_0123456.zip", fileName);
        Assert.IsTrue(GameUpdatePackageNaming.Matches(fileName, "1.2.3", revision));
        Assert.IsFalse(GameUpdatePackageNaming.Matches(
            "TaikoDive-v1.2.3.zip",
            "1.2.3",
            revision));
        Assert.IsFalse(GameUpdatePackageNaming.IsVersion("999999999999.1.1"));
        Assert.AreEqual(
            GamePackageKeyProvider.DerivePassword("secret", fileName),
            GamePackageKeyProvider.DerivePassword("secret", fileName));
        Assert.AreNotEqual(
            GamePackageKeyProvider.DerivePassword("secret", fileName),
            GamePackageKeyProvider.DerivePassword("secret", GameUpdatePackageNaming.Create("1.2.4", revision)));
    }

    [TestMethod]
    public void AssetUpdatePackageNameAndManifestUseIndependentChannel()
    {
        string revision = "0123456789abcdef0123456789abcdef01234567";
        string packageName = AssetUpdatePackageNaming.Create("2.3.4", revision);
        Assert.AreEqual("TaikoDive_Assets_v2.3.4_win-x64_0123456.zip", packageName);
        Assert.IsTrue(AssetUpdatePackageNaming.Matches(packageName, "2.3.4", revision));
        Assert.IsFalse(GameUpdatePackageNaming.Matches(packageName, "2.3.4", revision));

        GameUpdateManifest manifest = new()
        {
            Version = "2.3.4",
            Revision = revision,
            PackageFileName = packageName,
            PackageUrl = $"https://github.com/Hamaryo226/TaikoDive-Launcher/releases/download/assets-stable/{packageName}",
            Sha256 = new string('A', 64),
            Size = 123,
            PublishedAt = DateTimeOffset.UtcNow,
            Archive = new GameUpdateArchiveInfo
            {
                Format = "zip",
                Encryption = "winzip-aes-256",
                Payload = "payload.bin",
                KeyId = "2026-01",
            },
        };

        GameUpdateService.ValidateAssetManifest(manifest);
        Assert.ThrowsExactly<InvalidDataException>(() => GameUpdateService.ValidateManifest(manifest));
    }

    [TestMethod]
    public void GameUpdateManifestRequiresCanonicalEncryptedPackage()
    {
        string revision = "0123456789abcdef0123456789abcdef01234567";
        string packageName = GameUpdatePackageNaming.Create("1.2.3", revision);
        GameUpdateManifest manifest = new()
        {
            Version = "1.2.3",
            Revision = revision,
            PackageFileName = packageName,
            PackageUrl = $"https://github.com/Hamaryo226/TaikoDive-Launcher/releases/download/game-stable/{packageName}",
            Sha256 = new string('A', 64),
            Size = 123,
            PublishedAt = DateTimeOffset.UtcNow,
            Archive = new GameUpdateArchiveInfo
            {
                Format = "zip",
                Encryption = "winzip-aes-256",
                Payload = "payload.bin",
                KeyId = "2026-01",
            },
        };

        GameUpdateService.ValidateManifest(manifest);
        manifest.PackageFileName = "TaikoDive_Update_v1.2.3.zip";
        Assert.ThrowsExactly<InvalidDataException>(() => GameUpdateService.ValidateManifest(manifest));
    }

    [TestMethod]
    public async Task GameUpdateCheckerFindsNewerPublicPackage()
    {
        using TemporaryInstallation temporary = new();
        string revision = "0123456789abcdef0123456789abcdef01234567";
        string packageName = GameUpdatePackageNaming.Create("9.8.7", revision);
        string manifest = $$"""
            {
              "version": "9.8.7",
              "revision": "{{revision}}",
              "releaseNotes": "・オンラインプレイを改善",
              "packageFileName": "{{packageName}}",
              "packageUrl": "https://github.com/Hamaryo226/TaikoDive-Launcher/releases/download/game-stable/{{packageName}}",
              "sha256": "{{new string('A', 64)}}",
              "size": 1234,
              "publishedAt": "2026-08-23T00:00:00Z",
              "archive": {
                "format": "zip",
                "encryption": "winzip-aes-256",
                "payload": "payload.bin",
                "keyId": "2026-01"
              }
            }
            """;
        using HttpClient client = new(new StaticResponseHandler(manifest));
        GameUpdateService service = new(
            client,
            new Uri("https://example.test/game-update-manifest.json"),
            () => temporary.Installation,
            () => false,
            () => "test-package-key",
            new GameUpdatePackageExtractor());

        await service.CheckAsync();

        Assert.AreEqual(GameUpdateState.Available, service.State);
        Assert.IsNotNull(service.AvailableUpdate);
        Assert.AreEqual("9.8.7", service.AvailableUpdate.Version);
        Assert.IsNotNull(service.LatestUpdate);
        Assert.AreEqual("・オンラインプレイを改善", service.LatestUpdate.ReleaseNotes);
    }

    [TestMethod]
    public async Task GameUpdateCheckerTreatsExecutableProductVersionAsUnmanagedBaseline()
    {
        using TemporaryInstallation temporary = new(typeof(PersistenceTests).Assembly.Location);
        string? productVersion = FileVersionInfo.GetVersionInfo(temporary.Installation.ExecutablePath).ProductVersion;
        Assert.IsTrue(Regex.IsMatch(productVersion ?? string.Empty, @"\d+\.\d+\.\d+"));

        string revision = "0123456789abcdef0123456789abcdef01234567";
        string packageName = GameUpdatePackageNaming.Create("0.1.0", revision);
        string manifest = $$"""
            {
              "version": "0.1.0",
              "revision": "{{revision}}",
              "packageFileName": "{{packageName}}",
              "packageUrl": "https://github.com/Hamaryo226/TaikoDive-Launcher/releases/download/game-stable/{{packageName}}",
              "sha256": "{{new string('A', 64)}}",
              "size": 1234,
              "publishedAt": "2026-08-23T00:00:00Z",
              "archive": {
                "format": "zip",
                "encryption": "winzip-aes-256",
                "payload": "payload.bin",
                "keyId": "2026-01"
              }
            }
            """;
        using HttpClient client = new(new StaticResponseHandler(manifest));
        GameUpdateService service = new(
            client,
            new Uri("https://example.test/game-update-manifest.json"),
            () => temporary.Installation,
            () => false,
            () => "test-package-key",
            new GameUpdatePackageExtractor());

        await service.CheckAsync();

        Assert.AreEqual(GameUpdateState.Available, service.State);
        Assert.AreEqual("0.1.0", service.AvailableUpdate?.Version);
    }

    [TestMethod]
    public void GameUpdateRejectsUserDataPaths()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => GameUpdatePathPolicy.NormalizeAndValidate("Setting.json"));
        Assert.ThrowsExactly<InvalidDataException>(() => GameUpdatePathPolicy.NormalizeAndValidate("Info/User.ini"));
        Assert.ThrowsExactly<InvalidDataException>(() => GameUpdatePathPolicy.NormalizeAndValidate("Songs/song/song.ogg"));
        Assert.ThrowsExactly<InvalidDataException>(() => GameUpdatePathPolicy.NormalizeAndValidate("Screenshot/result.png"));
        Assert.ThrowsExactly<InvalidDataException>(() => GameUpdatePathPolicy.NormalizeAndValidate("Log.txt"));
        Assert.ThrowsExactly<InvalidDataException>(() => GameUpdatePathPolicy.NormalizeAndValidate("../outside.dll"));
        Assert.AreEqual("Texture/NewAsset.png", GameUpdatePathPolicy.NormalizeAndValidate("Texture/NewAsset.png"));
    }

    [TestMethod]
    public void AssetUpdateAllowsManagedSongsButRejectsUserProfile()
    {
        Assert.AreEqual(
            "Songs/09 段位道場.7z",
            AssetUpdatePathPolicy.NormalizeAndValidate("Songs/09 段位道場.7z"));
        Assert.ThrowsExactly<InvalidDataException>(() => AssetUpdatePathPolicy.NormalizeAndValidate("Info/User.ini"));
        Assert.ThrowsExactly<InvalidDataException>(() => AssetUpdatePathPolicy.NormalizeAndValidate("Info/Chara/Don/Chara.png"));
        Assert.ThrowsExactly<InvalidDataException>(() => AssetUpdatePathPolicy.NormalizeAndValidate("info/chara/Don/Chara.png"));
        Assert.ThrowsExactly<InvalidDataException>(() => AssetUpdatePathPolicy.NormalizeAndValidate("../outside.png"));
    }

    [TestMethod]
    public async Task GameUpdateRollsBackAlreadyReplacedFilesWhenApplyFails()
    {
        string root = Path.Combine(Path.GetTempPath(), "TaikoDiveLauncher.Tests", Guid.NewGuid().ToString("N"));
        string staged = Path.Combine(root, "staged");
        string target = Path.Combine(root, "target");
        string backup = Path.Combine(root, "backup");
        Directory.CreateDirectory(staged);
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(staged, "first.dll"), "new");
        await File.WriteAllTextAsync(Path.Combine(target, "first.dll"), "old");
        try
        {
            IReadOnlyList<GamePackageFile> files =
            [
                new() { Path = "first.dll", Size = 3, Sha256 = new string('A', 64) },
                new() { Path = "missing.dll", Size = 3, Sha256 = new string('B', 64) },
            ];

            await Assert.ThrowsExactlyAsync<FileNotFoundException>(() => GameUpdateService.ApplyWithRollbackAsync(
                staged,
                target,
                backup,
                files,
                CancellationToken.None));

            Assert.AreEqual("old", await File.ReadAllTextAsync(Path.Combine(target, "first.dll")));
            Assert.IsFalse(File.Exists(Path.Combine(target, "missing.dll")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task GameUpdateExtractorReadsWinZipAes256Payload()
    {
        string sevenZip = @"C:\Program Files\7-Zip\7z.exe";
        if (!File.Exists(sevenZip))
        {
            Assert.Inconclusive("7-Zip is required for the AES integration test.");
        }

        string root = Path.Combine(Path.GetTempPath(), "TaikoDiveLauncher.Tests", Guid.NewGuid().ToString("N"));
        string payloadRoot = Path.Combine(root, "payload-root");
        string output = Path.Combine(root, "output");
        Directory.CreateDirectory(Path.Combine(payloadRoot, "Texture"));
        Directory.CreateDirectory(output);
        string assetPath = Path.Combine(payloadRoot, "Texture", "new.txt");
        await File.WriteAllTextAsync(assetPath, "new-asset");
        string hash = await GameUpdatePackageExtractor.ComputeSha256Async(assetPath, CancellationToken.None);
        string revision = new('a', 40);
        string packageName = GameUpdatePackageNaming.Create("1.2.3", revision);
        GamePackageManifest internalManifest = new()
        {
            Version = "1.2.3",
            Revision = revision,
            Files = [new() { Path = "Texture/new.txt", Sha256 = hash, Size = new FileInfo(assetPath).Length }],
        };
        await File.WriteAllTextAsync(
            Path.Combine(payloadRoot, "package-files.json"),
            System.Text.Json.JsonSerializer.Serialize(internalManifest, LauncherJsonContext.Default.GamePackageManifest));
        string payloadPath = Path.Combine(root, "payload.bin");
        ZipFile.CreateFromDirectory(payloadRoot, payloadPath);
        string packagePath = Path.Combine(root, packageName);
        string key = "01234567890123456789012345678901";
        string password = GamePackageKeyProvider.DerivePassword(key, packageName);
        try
        {
            ProcessStartInfo startInfo = new(sevenZip)
            {
                UseShellExecute = false,
                WorkingDirectory = root,
            };
            foreach (string argument in new[] { "a", "-tzip", "-mem=AES256", $"-p{password}", "-bd", "-bso0", "-bsp0", packagePath, payloadPath })
            {
                startInfo.ArgumentList.Add(argument);
            }
            using Process process = Process.Start(startInfo)!;
            await process.WaitForExitAsync();
            Assert.AreEqual(0, process.ExitCode);

            FileInfo packageFile = new(packagePath);
            GameUpdateManifest update = new()
            {
                Version = "1.2.3",
                Revision = revision,
                PackageFileName = packageName,
                Sha256 = await GameUpdatePackageExtractor.ComputeSha256Async(packagePath, CancellationToken.None),
                Size = packageFile.Length,
                PublishedAt = DateTimeOffset.UtcNow,
                PackageUrl = $"https://github.com/example/example/releases/download/game-stable/{packageName}",
                Archive = new()
                {
                    Format = "zip",
                    Encryption = "winzip-aes-256",
                    Payload = "payload.bin",
                    KeyId = "2026-01",
                },
            };
            GameUpdatePackageExtractor extractor = new();
            GamePackageManifest result = await extractor.ExtractAndVerifyAsync(
                packagePath,
                output,
                update,
                key,
                CancellationToken.None);

            Assert.HasCount(1, result.Files);
            Assert.AreEqual("new-asset", await File.ReadAllTextAsync(Path.Combine(output, "files", "Texture", "new.txt")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void CreateZip(string path, params (string Path, string Content)[] files)
    {
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string entryPath, string content) in files)
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryPath);
            using StreamWriter writer = new(entry.Open());
            writer.Write(content);
        }
    }

    private sealed class StaticResponseHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class TemporaryInstallation : IDisposable
    {
        public TemporaryInstallation(string? executableSource = null)
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "TaikoDiveLauncher.Tests", Guid.NewGuid().ToString("N"));
            string buildDirectory = Path.Combine(RootDirectory, "build");
            Directory.CreateDirectory(Path.Combine(buildDirectory, "Info"));
            string executablePath = Path.Combine(buildDirectory, "TaikoDive.exe");
            if (string.IsNullOrWhiteSpace(executableSource))
            {
                File.WriteAllBytes(executablePath, [0]);
            }
            else
            {
                File.Copy(executableSource, executablePath);
            }
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
