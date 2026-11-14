using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LauncherPreferencesProtectionTests
{
    private string _directory = null!;
    private string? _previous;
    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "preferences-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _previous = Environment.GetEnvironmentVariable("TAIKODIVE_LAUNCHER_SETTINGS_DIRECTORY");
        Environment.SetEnvironmentVariable("TAIKODIVE_LAUNCHER_SETTINGS_DIRECTORY", _directory);
    }
    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("TAIKODIVE_LAUNCHER_SETTINGS_DIRECTORY", _previous);
        Directory.Delete(_directory, true);
    }

    [TestMethod]
    [DataRow("{\"future\":42}")]
    [DataRow("{\"theme\":\"Future\"}")]
    [DataRow("{\"homeBannerStyle\":\"Future\"}")]
    [DataRow("{\"windowPlacement\":{\"future\":1}}")]
    [DataRow("null")]
    [DataRow("{\"theme\":\"Dark\",\"theme\":\"Light\"}")]
    public async Task UnsupportedSettingsRemainProtected(string json)
    {
        var store = new LauncherPreferencesStore();
        await File.WriteAllTextAsync(store.FilePath, json);
        await store.LoadAsync();
        Assert.IsFalse(store.CanSave);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(new()));
        Assert.AreEqual(json, await File.ReadAllTextAsync(store.FilePath));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task BackupVerificationFailureAbortsReset(bool throwRead)
    {
        var store = new LauncherPreferencesStore();
        const string original = "{\"future\":42}";
        await File.WriteAllTextAsync(store.FilePath, original);
        await store.LoadAsync();
        int reads = 0;
        Func<string, Task<byte[]>> read = path =>
        {
            reads++;
            StringAssert.Contains(path, ".backup");
            Assert.AreEqual(original, File.ReadAllText(store.FilePath));
            return throwRead ? Task.FromException<byte[]>(new IOException("backup read failed"))
                : Task.FromResult(new byte[] { 0 });
        };
        store.ReadVerificationBytesAsync = read;
        await Assert.ThrowsAsync<IOException>(() => store.BackupAndResetAsync());
        Assert.AreEqual(1, reads);
        Assert.AreEqual(original, await File.ReadAllTextAsync(store.FilePath));
        Assert.IsFalse(store.CanSave);
        Assert.IsNull(store.LastBackupPath);
        StringAssert.Contains(store.ProtectionReason!, "退避できなかった");
        Assert.IsFalse(File.Exists(store.FilePath + ".launcher.tmp"));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task PublishedVerificationFailureProtectsUnknownOutcome(bool throwRead, bool reset)
    {
        var store = new LauncherPreferencesStore();
        const string original = "{\"theme\":\"Light\"}";
        await File.WriteAllTextAsync(store.FilePath, original);
        await store.LoadAsync();
        int publishedReads = 0;
        store.ReadVerificationBytesAsync = async path =>
        {
            if (path != store.FilePath) return await File.ReadAllBytesAsync(path);
            publishedReads++;
            Assert.AreNotEqual(original, await File.ReadAllTextAsync(path), "Inject only after actual publication");
            if (throwRead) throw new IOException("published read failed");
            return [0];
        };
        await Assert.ThrowsAsync<IOException>(async () =>
        {
            if (reset) await store.BackupAndResetAsync();
            else await store.SaveAsync(new());
        });
        Assert.AreEqual(1, publishedReads);
        Assert.IsFalse(store.CanSave);
        StringAssert.Contains(store.ProtectionReason!, "保存結果を確認できませんでした");
        string published = await File.ReadAllTextAsync(store.FilePath);
        Assert.AreNotEqual(original, published);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(new() { Theme = "Light" }));
        Assert.AreEqual(published, await File.ReadAllTextAsync(store.FilePath));
        Assert.IsFalse(File.Exists(store.FilePath + ".launcher.tmp"));
        if (reset)
        {
            Assert.IsNotNull(store.LastBackupPath);
            Assert.AreEqual(original, await File.ReadAllTextAsync(store.LastBackupPath));
        }
        await store.LoadAsync();
        Assert.IsTrue(store.CanSave);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FailedSaveEditsSurviveCancelledUpdateAndRequireExplicitDiscard(bool consent)
    {
        var context = new LauncherContext();
        await context.InitializeAsync();
        context.Preferences.Theme = "Light";
        Directory.CreateDirectory(Path.Combine(_directory, "launcher.json.launcher.tmp"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.SavePreferencesAsync());
        Assert.IsTrue(context.HasUnsavedPreferenceEdits);
        int installers = 0, exits = 0;
        await context.PreferenceTransitions.RunUpdateAsync(() => Task.FromResult(consent),
            context.RevertPreferenceEdits, () =>
            {
                installers++;
                Assert.IsFalse(context.HasUnsavedPreferenceEdits);
                Assert.IsTrue(context.PreferenceTransitions.IsBusy);
                return Task.FromResult(true);
            }, () => Task.CompletedTask, () => exits++);
        Assert.AreEqual(consent ? 1 : 0, installers);
        Assert.AreEqual(consent ? 1 : 0, exits);
        Assert.AreEqual(!consent, context.HasUnsavedPreferenceEdits);
        Assert.AreEqual(consent ? "Dark" : "Light", context.Preferences.Theme);
        Assert.IsFalse(File.Exists(Path.Combine(_directory, "launcher.json")));
    }

    [TestMethod]
    public async Task ReadFailureProtectsSaving()
    {
        var store = new LauncherPreferencesStore();
        await File.WriteAllTextAsync(store.FilePath, "{}");
        using (var locked = new FileStream(store.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await store.LoadAsync();
        Assert.IsFalse(store.CanSave);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(new()));
    }

    [TestMethod]
    public async Task StaleInstanceCannotOverwriteExternalChange()
    {
        var first = new LauncherPreferencesStore();
        var second = new LauncherPreferencesStore();
        await first.LoadAsync();
        await second.LoadAsync();
        await first.SaveAsync(new() { Theme = "Light" });
        string saved = await File.ReadAllTextAsync(first.FilePath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => second.SaveAsync(new()));
        Assert.IsFalse(second.CanSave);
        Assert.AreEqual(saved, await File.ReadAllTextAsync(first.FilePath));
        await second.LoadAsync();
        await second.SaveAsync(new());
    }

    [TestMethod]
    public async Task CooperatingWriterWaitsForExclusiveLease()
    {
        var store = new LauncherPreferencesStore();
        await store.LoadAsync();
        Task save;
        using (var lease = new FileStream(store.FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            save = store.SaveAsync(new());
            await Task.Delay(150);
            Assert.IsFalse(save.IsCompleted, "Save must wait for the cooperating process lease");
            Assert.IsFalse(File.Exists(store.FilePath));
        }
        await save;
    }

    [TestMethod]
    public async Task SimultaneousInstancesHaveExactlyOneWinner()
    {
        var stores = Enumerable.Range(0, 12).Select(_ => new LauncherPreferencesStore()).ToArray();
        foreach (var store in stores) await store.LoadAsync();
        var results = await Task.WhenAll(stores.Select(async (store, index) =>
        {
            try { await store.SaveAsync(new() { WindowPlacement = new() { X = index } }); return true; }
            catch (InvalidOperationException) { return false; }
        }));
        Assert.AreEqual(1, results.Count(result => result));
    }

    [TestMethod]
    public async Task ReplacementFailureProtectsUntilReload()
    {
        var store = new LauncherPreferencesStore();
        await File.WriteAllTextAsync(store.FilePath, "{}");
        await store.LoadAsync();
        using (var locked = new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.SaveAsync(new()));
        Assert.IsFalse(store.CanSave, "A failed replacement does not prove the original was retained");
        await store.LoadAsync();
        Assert.IsTrue(store.CanSave);
    }

    [TestMethod]
    public async Task OrdinaryStagingFailureAllowsRetry()
    {
        var store = new LauncherPreferencesStore();
        await store.LoadAsync();
        Directory.CreateDirectory(store.FilePath + ".launcher.tmp");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.SaveAsync(new()));
        Assert.IsTrue(store.CanSave);
        Assert.IsFalse(File.Exists(store.FilePath));
        Directory.Delete(store.FilePath + ".launcher.tmp");
        await store.SaveAsync(new());
    }

    private static async Task<string?> ResetAsync(LauncherPreferencesStore store)
    {
        return await store.BackupAndResetAsync();
    }

    [TestMethod]
    public async Task ResetPreservesExactCorruptBytesAndEnablesSaving()
    {
        var store = new LauncherPreferencesStore();
        byte[] bytes = [0xff, 0xfe, 0x7b, 0, 0x21, 0];
        await File.WriteAllBytesAsync(store.FilePath, bytes);
        await store.LoadAsync();
        string? backup = await ResetAsync(store);
        Assert.IsNotNull(backup);
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(backup));
        Assert.IsTrue(store.CanSave);
        Assert.AreEqual("Dark", (await store.LoadAsync()).Theme);
    }

    [TestMethod]
    public async Task BackupCollisionDoesNotOverwritePreviousBackup()
    {
        var store = new LauncherPreferencesStore();
        await File.WriteAllTextAsync(store.FilePath, "{bad");
        await File.WriteAllTextAsync(store.FilePath + ".backup", "previous backup");
        await store.LoadAsync();
        string? backup = await store.BackupAndResetAsync();
        Assert.AreNotEqual(store.FilePath + ".backup", backup);
        Assert.AreEqual("previous backup", await File.ReadAllTextAsync(store.FilePath + ".backup"));
        Assert.AreEqual("{bad", await File.ReadAllTextAsync(backup!));
    }

    [TestMethod]
    public async Task BackupFailureAbortsReset()
    {
        var store = new LauncherPreferencesStore();
        await File.WriteAllTextAsync(store.FilePath, "{bad");
        await store.LoadAsync();
        Directory.CreateDirectory(store.FilePath + ".backup");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.BackupAndResetAsync());
        Assert.AreEqual("{bad", await File.ReadAllTextAsync(store.FilePath));
        Assert.IsFalse(store.CanSave);
        Assert.IsNull(store.LastBackupPath);
    }

    [TestMethod]
    public async Task FirstPublicationIsCompleteJson()
    {
        var store = new LauncherPreferencesStore();
        await store.LoadAsync();
        var save = store.SaveAsync(new());
        while (!save.IsCompleted)
        {
            if (File.Exists(store.FilePath))
            {
                try
                {
                    using var stream = new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var json = await System.Text.Json.JsonDocument.ParseAsync(stream);
                    Assert.AreEqual("Dark", json.RootElement.GetProperty("theme").GetString());
                    Assert.IsTrue(json.RootElement.TryGetProperty("closeAfterLaunch", out _));
                }
                catch (IOException ex) when ((ex.HResult & 0xffff) == 32) { /* Rename briefly holds a Windows sharing lock. */ }
            }
            await Task.Yield();
        }
        await save;
        using var final = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(store.FilePath));
        Assert.AreEqual("Ocean", final.RootElement.GetProperty("homeBannerStyle").GetString());
    }

    [TestMethod]
    public async Task ReloadWaitsForCooperatingWriter()
    {
        var store = new LauncherPreferencesStore();
        Task<LauncherPreferences> load;
        using (var lease = new FileStream(store.FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            load = store.LoadAsync();
            await Task.Delay(150);
            Assert.IsFalse(load.IsCompleted);
        }
        await load;
        Assert.IsTrue(store.CanSave);
    }

    [TestMethod]
    public async Task DirectoryAtSettingsPathIsReadFailureNotFirstRun()
    {
        var store = new LauncherPreferencesStore();
        Directory.CreateDirectory(store.FilePath);
        await store.LoadAsync();
        Assert.IsFalse(store.CanSave);
    }

    [TestMethod]
    public async Task FailedEditCanRevertToLastConfirmedValuesWithoutWriting()
    {
        var context = new LauncherContext();
        await context.InitializeAsync();
        context.Preferences.Theme = "Light";
        await context.SavePreferencesAsync();
        context.Preferences.Theme = "Dark";
        Directory.CreateDirectory(Path.Combine(_directory, "launcher.json.launcher.tmp"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.SavePreferencesAsync());
        Assert.AreEqual("Dark", context.Preferences.Theme);
        Assert.IsTrue(context.HasUnsavedPreferenceEdits);
        context.RevertPreferenceEdits();
        Assert.IsFalse(context.HasUnsavedPreferenceEdits);
        Assert.AreEqual("Light", context.Preferences.Theme);
        StringAssert.Contains(await File.ReadAllTextAsync(Path.Combine(_directory, "launcher.json")), "Light");
    }

    [TestMethod]
    public async Task FailedReloadKeepsEditsAndSuccessfulReloadReplacesThem()
    {
        var context = new LauncherContext();
        await context.InitializeAsync();
        context.Preferences.Theme = "Light";
        await File.WriteAllTextAsync(Path.Combine(_directory, "launcher.json"), "{bad");
        Assert.IsFalse(await context.ReloadPreferencesAsync());
        Assert.AreEqual("Light", context.Preferences.Theme);
        Assert.IsFalse(context.CanSavePreferences);
        await File.WriteAllTextAsync(Path.Combine(_directory, "launcher.json"), """{"homeBannerStyle":"Violet"}""");
        Assert.IsTrue(await context.ReloadPreferencesAsync());
        Assert.AreEqual("Violet", context.Preferences.HomeBannerStyle);
        Assert.IsFalse(context.HasUnsavedPreferenceEdits);
    }

    [TestMethod]
    public async Task PlacementSaveDoesNotPersistFailedEditsOrStaleReloadBounds()
    {
        var context = new LauncherContext();
        await context.InitializeAsync();
        context.Preferences.Theme = "Light"; // unsaved edit
        await context.SavePlacementAsync(new WindowPlacementPreferences { X = 42, Width = 900, Height = 700 }, 0);
        var store = new LauncherPreferencesStore();
        var saved = await store.LoadAsync();
        Assert.AreEqual("Dark", saved.Theme);
        Assert.AreEqual(42, saved.WindowPlacement!.X);
        Assert.IsTrue(context.HasUnsavedPreferenceEdits);
        Assert.IsTrue(await context.ReloadPreferencesAsync());
        await context.SavePlacementAsync(new WindowPlacementPreferences { X = 99 }, 0);
        Assert.AreEqual(42, (await store.LoadAsync()).WindowPlacement!.X);
    }

    [TestMethod]
    public async Task AnimationPreferenceRoundTripsWithoutChangingForOsPolicy()
    {
        var store = new LauncherPreferencesStore();
        var preferences = await store.LoadAsync();
        Assert.IsTrue(preferences.BackgroundAnimationEnabled);
        preferences.BackgroundAnimationEnabled = false;
        await store.SaveAsync(preferences);
        Assert.IsFalse((await store.LoadAsync()).BackgroundAnimationEnabled);
    }

    [TestMethod]
    public async Task UnreadablePreSaveComparisonProtectsSaving()
    {
        var store = new LauncherPreferencesStore();
        await File.WriteAllTextAsync(store.FilePath, "{}");
        await store.LoadAsync();
        using (var locked = new FileStream(store.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(new()));
        Assert.IsFalse(store.CanSave);
    }

    [TestMethod]
    public async Task ByteOnlyExternalChangeIsNotOverwritten()
    {
        var store = new LauncherPreferencesStore();
        await File.WriteAllTextAsync(store.FilePath, "{}", new System.Text.UTF8Encoding(false));
        await store.LoadAsync();
        await File.WriteAllTextAsync(store.FilePath, "{}", new System.Text.UTF8Encoding(true));
        byte[] external = await File.ReadAllBytesAsync(store.FilePath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(new()));
        CollectionAssert.AreEqual(external, await File.ReadAllBytesAsync(store.FilePath));
    }

    [TestMethod]
    public async Task ResetReplacementFailureReportsVerifiedBackupAndStaysProtected()
    {
        var store = new LauncherPreferencesStore();
        await File.WriteAllTextAsync(store.FilePath, "{bad");
        await store.LoadAsync();
        using (var locked = new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.BackupAndResetAsync());
        Assert.IsFalse(store.CanSave);
        Assert.IsNotNull(store.LastBackupPath);
        Assert.AreEqual("{bad", await File.ReadAllTextAsync(store.LastBackupPath));
        StringAssert.Contains(store.ProtectionReason!, "保存結果を確認できませんでした");
    }

    [TestMethod]
    public async Task ResetRejectsChangedFileUntilReload()
    {
        var store = new LauncherPreferencesStore();
        await File.WriteAllTextAsync(store.FilePath, "{bad");
        await store.LoadAsync();
        await File.WriteAllTextAsync(store.FilePath, "{new external");
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.BackupAndResetAsync());
        Assert.IsFalse(store.CanSave);
        Assert.AreEqual("{new external", await File.ReadAllTextAsync(store.FilePath));
        Assert.IsNull(store.LastBackupPath);
    }

    [TestMethod]
    public async Task CorruptSettingsCannotBeOverwrittenByFallback()
    {
        var store = new LauncherPreferencesStore();
        await File.WriteAllTextAsync(store.FilePath, "{broken");
        var fallback = await store.LoadAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(fallback));
        Assert.AreEqual("{broken", await File.ReadAllTextAsync(store.FilePath));
    }
}
