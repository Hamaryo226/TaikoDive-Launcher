using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Tests;

[TestClass]
public sealed class ToolkitTests
{
    [TestMethod]
    public async Task ResumeRequestsOnlyRemainingBytesAndVerifiesWholeFile()
    {
        using var temp = new Temp(); byte[] bytes = Encoding.UTF8.GetBytes("abcdefghij");
        string target = Path.Combine(temp.Root, "test.zip"); await File.WriteAllBytesAsync(target + ".partial", bytes[..4]);
        using var client = new HttpClient(new Handler(request =>
        {
            Assert.AreEqual(4L, request.Headers.Range!.Ranges.Single().From);
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(bytes[4..]) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(4, 9, 10); return response;
        }));
        await ResumableDownload.FetchAsync(client, new Uri("https://example.test/file"), target, bytes.Length, Hash(bytes), null, default);
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(target)); Assert.IsFalse(File.Exists(target + ".partial"));
    }

    [TestMethod]
    public async Task ServerIgnoringRangeRestartsWithoutAppending()
    {
        using var temp = new Temp(); byte[] bytes = Encoding.UTF8.GetBytes("abcdefghij");
        string target = Path.Combine(temp.Root, "test.zip"); await File.WriteAllBytesAsync(target + ".partial", bytes[..4]);
        using var client = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
        await ResumableDownload.FetchAsync(client, new Uri("https://example.test/file"), target, bytes.Length, Hash(bytes), null, default);
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(target));
    }

    [TestMethod]
    public async Task BadResumeOffsetCannotCorruptPartialFile()
    {
        using var temp = new Temp(); string target = Path.Combine(temp.Root, "test.zip");
        await File.WriteAllTextAsync(target + ".partial", "abc");
        using var client = new HttpClient(new Handler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(new byte[4]) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 3, 4); return response;
        }));
        await Assert.ThrowsAsync<InvalidDataException>(() => ResumableDownload.FetchAsync(client, new("https://example.test/file"), target, 4, Hash(new byte[4]), null, default));
        Assert.AreEqual("abc", await File.ReadAllTextAsync(target + ".partial")); Assert.IsFalse(File.Exists(target));
    }

    [TestMethod]
    public async Task HashMismatchDeletesPartialAndNeverPublishesFile()
    {
        using var temp = new Temp(); string target = Path.Combine(temp.Root, "test.zip");
        using var client = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) }));
        await Assert.ThrowsAsync<InvalidDataException>(() => ResumableDownload.FetchAsync(client, new("https://example.test/file"), target, 3, Hash([3, 2, 1]), null, default));
        Assert.IsFalse(File.Exists(target)); Assert.IsFalse(File.Exists(target + ".partial"));
    }

    [TestMethod]
    public async Task CompleteVerifiedCacheDoesNotMakeNetworkRequest()
    {
        using var temp = new Temp(); string target = Path.Combine(temp.Root, "test.zip"); await File.WriteAllBytesAsync(target, [1, 2, 3]);
        using var client = new HttpClient(new Handler(_ => throw new AssertFailedException("Unexpected HTTP request")));
        await ResumableDownload.FetchAsync(client, new("https://example.test/file"), target, 3, Hash([1, 2, 3]), null, default);
    }

    [TestMethod]
    public void SharedOutfitCannotChangeModelsPathAndRejectsInvalidIds()
    {
        string json = JsonSerializer.Serialize(new OutfitCollection { Presets = [new() { Name = "配信", Settings = new() { ModelsPath = "C:/outside", Head = "head_000001" } }] }, LauncherJsonContext.Default.OutfitCollection);
        var parsed = LauncherToolkitStore.ParseOutfits(json);
        Assert.AreEqual("Info/Chara/Model", parsed.Presets[0].Settings.ModelsPath);
        Assert.Throws<InvalidDataException>(() => LauncherToolkitStore.ParseOutfits(json.Replace("head_000001", "../escape")));
        Assert.Throws<InvalidDataException>(() => LauncherToolkitStore.ParseOutfits(json.Replace("#00A7BE", "invalid")));
    }

    [TestMethod]
    public void OutfitAssetCheckIncludesBaseModelsAndUsesCurrentCustomRoot()
    {
        using var temp = new Temp(); var installation = new TaikoDiveInstallation(temp.Root);
        Assert.IsNotEmpty(LauncherToolkitStore.MissingAssets(installation, new()));
        string root = Path.Combine(temp.Root, "CustomModels");
        foreach (string relative in new[] { "animations.glb", "face/face_000000.png", "head/0.glb", "body/0.glb" })
        {
            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, []);
        }
        Assert.IsEmpty(LauncherToolkitStore.MissingAssets(installation, new(), "CustomModels"));
    }

    [TestMethod]
    public async Task SongScanReadsShiftJisAndMissingAudioWithoutChangingCharts()
    {
        using var temp = new Temp(); string chart = "TITLE:テスト\nWAVE:missing.ogg\nDEMOSTART:12.5\nCOURSE:Oni\n#START\n1111,\n#END";
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        await File.WriteAllTextAsync(Path.Combine(temp.Root, "a.tja"), chart, Encoding.GetEncoding(932));
        File.Copy(Path.Combine(temp.Root, "a.tja"), Path.Combine(temp.Root, "b.tja"));
        var songs = await SongLibraryService.ScanAsync(temp.Root);
        Assert.HasCount(2, songs); Assert.AreEqual("テスト", songs[0].Title); Assert.AreEqual(12.5, songs[0].DemoStart);
        Assert.IsTrue(songs.All(s => s.Problems.Any(p => p.Contains("音源"))));
        Assert.AreEqual(chart, await File.ReadAllTextAsync(Path.Combine(temp.Root, "a.tja"), Encoding.GetEncoding(932)));
    }

    [TestMethod]
    public async Task SongScanRejectsAudioOutsideSongsAndBrokenChartMarkers()
    {
        using var temp = new Temp();
        await File.WriteAllTextAsync(Path.Combine(temp.Root, "a.tja"), "TITLE:A\nWAVE:../outside.ogg\n#START\n1,");
        var songs = await SongLibraryService.ScanAsync(temp.Root);
        Assert.HasCount(2, songs[0].Problems); Assert.IsNull(songs[0].AudioPath);
    }

    [TestMethod]
    public void CompatibilityFindsMissingRequirementsButAllowsLegacyManifests()
    {
        var manifest = new GameUpdateManifest(); Assert.IsEmpty(LauncherMaintenance.CheckCompatibility(manifest, "0.0.0", "0.0.0", "1.0.0"));
        manifest.MinimumAssetsVersion = "0.15.0";
        Assert.HasCount(1, LauncherMaintenance.CheckCompatibility(manifest, "0.16.0", "0.14.0", "1.0.0"));
        Assert.IsEmpty(LauncherMaintenance.CheckCompatibility(manifest, "0.16.0", "0.15.0", "1.0.0"));
    }

    [TestMethod]
    public async Task CommitFailureRollsBackFilesAndDeletesNewFiles()
    {
        using var temp = new Temp(); string target = Path.Combine(temp.Root, "target"), source = Path.Combine(temp.Root, "source"), backup = Path.Combine(temp.Root, "backup");
        Directory.CreateDirectory(target); Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(target, "old.txt"), "old"); await File.WriteAllTextAsync(Path.Combine(source, "old.txt"), "new"); await File.WriteAllTextAsync(Path.Combine(source, "added.txt"), "added");
        await Assert.ThrowsAsync<IOException>(() => GameUpdateService.ApplyWithRollbackAsync(source, target, backup,
            [new() { Path = "old.txt" }, new() { Path = "added.txt" }], default, commit: () => throw new IOException("metadata full")));
        Assert.AreEqual("old", await File.ReadAllTextAsync(Path.Combine(target, "old.txt"))); Assert.IsFalse(File.Exists(Path.Combine(target, "added.txt")));
    }

    [TestMethod]
    public async Task UpdateHistoryRestoresChangedAndDeletedFilesAndPreservesUserData()
    {
        using var fixture = new RollbackFixture(); await fixture.CreateAsync();
        var result = await fixture.Service.RestorePreviousAsync(fixture.Entry.Id);
        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.AreEqual("old", await File.ReadAllTextAsync(Path.Combine(fixture.Target, "old.txt")));
        Assert.AreEqual("deleted", await File.ReadAllTextAsync(Path.Combine(fixture.Target, "deleted.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Target, "added.txt")));
        Assert.AreEqual("user settings", await File.ReadAllTextAsync(Path.Combine(fixture.Target, "Setting.json")));
        var state = JsonSerializer.Deserialize(await File.ReadAllTextAsync(Path.Combine(fixture.Updates, "installed.json")), LauncherJsonContext.Default.InstalledGameUpdate);
        Assert.AreEqual("1.0.0", state!.Version);
        Assert.IsTrue((await fixture.Service.GetHistoryAsync())[0].Reverted);
    }

    [TestMethod]
    public async Task CorruptBackupStopsRestoreBeforeAnyFileIsChanged()
    {
        using var fixture = new RollbackFixture(); await fixture.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(fixture.Entry.BackupDirectory, "old.txt"), "corrupt");
        var result = await fixture.Service.RestorePreviousAsync(fixture.Entry.Id);
        Assert.IsFalse(result.Succeeded); Assert.AreEqual("new", await File.ReadAllTextAsync(Path.Combine(fixture.Target, "old.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(fixture.Target, "added.txt")));
    }

    [TestMethod]
    public async Task HistoryRejectsProtectedPathsDuringRestore()
    {
        using var fixture = new RollbackFixture(); await fixture.CreateAsync();
        fixture.Entry.Changes.Add(new("Setting.json", "追加", 0)); await fixture.SaveHistoryAsync();
        var result = await fixture.Service.RestorePreviousAsync(fixture.Entry.Id);
        Assert.IsFalse(result.Succeeded); Assert.AreEqual("user settings", await File.ReadAllTextAsync(Path.Combine(fixture.Target, "Setting.json")));
        Assert.AreEqual("new", await File.ReadAllTextAsync(Path.Combine(fixture.Target, "old.txt")));
    }

    private sealed class RollbackFixture : IDisposable
    {
        private readonly Temp _temp = new();
        public string Target => Path.Combine(_temp.Root, "target");
        public string Updates => Path.Combine(_temp.Root, "game-updates");
        public UpdateHistoryEntry Entry { get; } = new() { Version = "2.0.0", Revision = new string('b', 40), InstalledAtUtc = DateTime.UtcNow };
        public GameUpdateService Service { get; }
        public RollbackFixture()
        {
            Service = new(new HttpClient(), new("https://example.test/manifest"), () => new(Target), () => false,
                () => "key", new GameUpdatePackageExtractor(), storageRoot: Updates);
        }
        public async Task CreateAsync()
        {
            Directory.CreateDirectory(Target); Entry.BuildDirectory = Target; Entry.BackupDirectory = Path.Combine(Updates, "backups", Entry.Id);
            Directory.CreateDirectory(Entry.BackupDirectory);
            await File.WriteAllTextAsync(Path.Combine(Target, "old.txt"), "new"); await File.WriteAllTextAsync(Path.Combine(Target, "added.txt"), "added");
            await File.WriteAllTextAsync(Path.Combine(Target, "Setting.json"), "user settings");
            await File.WriteAllTextAsync(Path.Combine(Entry.BackupDirectory, "old.txt"), "old"); await File.WriteAllTextAsync(Path.Combine(Entry.BackupDirectory, "deleted.txt"), "deleted");
            Entry.Changes = [new("old.txt", "変更", 3, Hash(Encoding.UTF8.GetBytes("old"))), new("added.txt", "追加", 5), new("deleted.txt", "削除", 7, Hash(Encoding.UTF8.GetBytes("deleted")))];
            Entry.PreviousInstalledState = JsonSerializer.Serialize(new InstalledGameUpdate { BuildDirectory = Target, Version = "1.0.0" }, LauncherJsonContext.Default.InstalledGameUpdate);
            await File.WriteAllTextAsync(Path.Combine(Updates, "installed.json"), JsonSerializer.Serialize(new InstalledGameUpdate { BuildDirectory = Target, Version = Entry.Version, Revision = Entry.Revision }, LauncherJsonContext.Default.InstalledGameUpdate));
            await SaveHistoryAsync();
        }
        public Task SaveHistoryAsync() => File.WriteAllTextAsync(Path.Combine(Updates, "history.json"), JsonSerializer.Serialize(new UpdateHistory { Entries = [Entry] }, LauncherJsonContext.Default.UpdateHistory));
        public void Dispose() => _temp.Dispose();
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(handle(request)); }
    private sealed class Temp : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "TaikoDiveToolkit_" + Guid.NewGuid().ToString("N"));
        public Temp() => Directory.CreateDirectory(Root);
        public void Dispose() { try { Directory.Delete(Root, true); } catch (IOException) { } }
    }
}
