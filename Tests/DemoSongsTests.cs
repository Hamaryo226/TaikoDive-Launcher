using System.Text;
using System.Text.Json;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Tests;

[TestClass]
public sealed class DemoSongsTests
{
    private sealed class Data : IDisposable
    {
        internal string Root = Path.Combine(Path.GetTempPath(), "Launcher-Demo-" + Guid.NewGuid().ToString("N"));
        internal TaikoDiveInstallation Installation => new(Root);
        internal string Chart(string name)
        {
            string folder = Path.Combine(Root, "Songs", "Genre");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "box.def"), "#GENRE:アニメ");
            File.WriteAllBytes(Path.Combine(folder, name + ".ogg"), [1]);
            File.WriteAllText(Path.Combine(folder, name + ".tja"), "TITLE:" + name + "\nSUBTITLE:--副題\nWAVE:" + name + ".ogg\nDEMOSTART:8.5\n#START\n1,\n#END");
            return "Genre/" + name + ".tja";
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }

    [TestMethod]
    public async Task SaveUsesPortablePathsAndPreservesThePreviousSelectionAsBackup()
    {
        using var data = new Data(); var store = new DemoSongSettingsStore(() => false);
        string first = data.Chart("一曲目"), second = data.Chart("二曲目");
        await store.SaveAsync(data.Installation, new() { Enabled = true, Entries = [new() { MainSong = second.Replace('/', '\\') }, new() { MainSong = first }] });
        var saved = await store.LoadAsync(data.Installation);
        CollectionAssert.AreEqual(new[] { second, first }, saved.Entries.Select(entry => entry.MainSong).ToArray());
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(DemoSongSettingsStore.SettingsPath(data.Installation)));
        Assert.AreEqual(2, json.RootElement.GetProperty("version").GetInt32());
        Assert.IsTrue(json.RootElement.GetProperty("enabled").GetBoolean());
        string original = await File.ReadAllTextAsync(DemoSongSettingsStore.SettingsPath(data.Installation));
        await store.SaveAsync(data.Installation, new() { Enabled = false });
        Assert.AreEqual(original, await File.ReadAllTextAsync(DemoSongSettingsStore.SettingsPath(data.Installation) + ".launcher.bak"));
        Assert.IsFalse((await store.LoadAsync(data.Installation)).Enabled);
    }

    [TestMethod]
    public async Task GameRunningAndInvalidAudioCannotOverwriteAnExistingSelection()
    {
        using var data = new Data(); string path = data.Chart("曲"); var store = new DemoSongSettingsStore(() => false);
        var settings = new DemoSongSettings { Enabled = true, Entries = [new() { MainSong = path }] };
        await store.SaveAsync(data.Installation, settings);
        string file = DemoSongSettingsStore.SettingsPath(data.Installation), before = await File.ReadAllTextAsync(file);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DemoSongSettingsStore(() => true).SaveAsync(data.Installation, new()));
        File.Delete(Path.Combine(data.Root, "Songs", "Genre", "曲.ogg"));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(data.Installation, settings));
        Assert.AreEqual(before, await File.ReadAllTextAsync(file));
    }

    [TestMethod]
    public async Task MissingChartRemainsVisibleWhenLoadingSavedSelections()
    {
        using var data = new Data(); string path = data.Chart("曲"); var store = new DemoSongSettingsStore(() => false);
        await store.SaveAsync(data.Installation, new() { Enabled = true, Entries = [new() { MainSong = path }] });
        File.Delete(Path.Combine(data.Root, "Songs", path));
        Assert.AreEqual(path, (await store.LoadAsync(data.Installation)).Entries.Single().MainSong);
    }

    [TestMethod]
    public async Task PlaylistRejectsOutsidePathsIncompleteManualEntriesAndTooManyEntries()
    {
        using var data = new Data(); string path = data.Chart("曲"); var store = new DemoSongSettingsStore(() => false);
        foreach (string outside in new[] { "../outside.tja", "/outside.tja", @"C:\outside.tja", @"\\server\share\a.tja", "Genre/a.ogg" })
            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(data.Installation, new() { Enabled = true, Entries = [new() { MainSong = outside }] }));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(data.Installation, new() { Enabled = true, Entries = [new() { MainSong = path, AutoSubSongs = false, SubSongs = [path, path, path] }] }));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(data.Installation, new() { Enabled = true, Entries = Enumerable.Range(0, 14).Select(_ => new DemoSongEntry { MainSong = path }).ToList() }));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(data.Installation, new() { Enabled = true }));
    }

    [TestMethod]
    public async Task LegacySelectionsBecomeIndependentEntriesWithoutWritingUntilSave()
    {
        using var data = new Data(); string a = data.Chart("一曲目"), b = data.Chart("二曲目");
        string file = DemoSongSettingsStore.SettingsPath(data.Installation); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string original = JsonSerializer.Serialize(new { version = 1, enabled = true, songs = new[] { b, a } });
        await File.WriteAllTextAsync(file, original);
        var store = new DemoSongSettingsStore(() => false); var settings = await store.LoadAsync(data.Installation);
        Assert.AreEqual(2, settings.Version); CollectionAssert.AreEqual(new[] { b, a }, settings.Entries.Select(entry => entry.MainSong).ToArray());
        Assert.IsTrue(settings.Entries.All(entry => entry.AutoSubSongs)); Assert.AreEqual(original, await File.ReadAllTextAsync(file));
        await store.SaveAsync(data.Installation, settings);
        Assert.AreEqual(original, await File.ReadAllTextAsync(file + ".launcher.bak"));
    }

    [TestMethod]
    public async Task PerEntryAutomaticModeKeepsManualChoicesAndDoesNotValidateHiddenMissingAudio()
    {
        using var data = new Data(); string a = data.Chart("メイン"), b = data.Chart("サブ"); var store = new DemoSongSettingsStore(() => false);
        var settings = new DemoSongSettings { Enabled = true, Entries = [
            new() { MainSong = a, AutoSubSongs = false, SubSongs = [b, a, b, a] },
            new() { MainSong = b, AutoSubSongs = true, SubSongs = ["Genre/missing.tja", "", "", ""] }] };
        await store.SaveAsync(data.Installation, settings); var loaded = await store.LoadAsync(data.Installation);
        Assert.IsFalse(loaded.Entries[0].AutoSubSongs); Assert.IsTrue(loaded.Entries[1].AutoSubSongs);
        CollectionAssert.AreEqual(settings.Entries[0].SubSongs, loaded.Entries[0].SubSongs);
        CollectionAssert.AreEqual(settings.Entries[1].SubSongs, loaded.Entries[1].SubSongs);
        loaded.Entries[1].AutoSubSongs = false;
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(data.Installation, loaded));
        settings.Enabled = false; await store.SaveAsync(data.Installation, settings);
        Assert.HasCount(2, (await store.LoadAsync(data.Installation)).Entries);
    }

    [TestMethod]
    public async Task ManualSubPathsAndAudioAreCheckedBeforeOverwritingSettings()
    {
        using var data = new Data(); string a = data.Chart("メイン"), b = data.Chart("サブ"); var store = new DemoSongSettingsStore(() => false);
        var entry = new DemoSongEntry { MainSong = a, AutoSubSongs = false, SubSongs = [b, b, b, b] };
        var settings = new DemoSongSettings { Enabled = true, Entries = [entry] };
        await store.SaveAsync(data.Installation, settings); string file = DemoSongSettingsStore.SettingsPath(data.Installation), original = await File.ReadAllTextAsync(file);
        entry.SubSongs[0] = "../outside.tja";
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(data.Installation, settings));
        entry.SubSongs[0] = b; File.Delete(Path.Combine(data.Root, "Songs", "Genre", "サブ.ogg"));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(data.Installation, settings));
        Assert.AreEqual(original, await File.ReadAllTextAsync(file));
    }

    [TestMethod]
    public async Task MalformedAudioPathDoesNotHideOtherSongs()
    {
        using var data = new Data(); data.Chart("有効"); string invalid = data.Chart("不正");
        await File.WriteAllTextAsync(Path.Combine(data.Root, "Songs", invalid), "TITLE:不正\nWAVE:\0bad.ogg\n#START\n1,\n#END");
        var songs = await SongLibraryService.ScanAsync(data.Installation.SongsDirectory);
        Assert.AreEqual(2, songs.Count);
        Assert.IsNotNull(songs.Single(s => s.Title == "有効").AudioPath);
        Assert.IsTrue(songs.Single(s => s.Title == "不正").Problems.Count > 0);
    }

    [TestMethod]
    public async Task SongCatalogReadsSubtitleAndInheritedGenreInBothEncodings()
    {
        using var data = new Data(); string first = data.Chart("日本語");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string full = Path.Combine(data.Root, "Songs", first);
        string text = await File.ReadAllTextAsync(full);
        await File.WriteAllTextAsync(full, text, Encoding.GetEncoding(932));
        var songs = await SongLibraryService.ScanAsync(data.Installation.SongsDirectory);
        var song = songs.Single();
        Assert.AreEqual("日本語", song.Title); Assert.AreEqual("副題", song.Subtitle);
        Assert.AreEqual("アニメ", song.Genre); Assert.AreEqual(8.5, song.DemoStart);
    }
}
