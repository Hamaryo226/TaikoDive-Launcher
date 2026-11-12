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
    public async Task LibraryDetectsDuplicatesAndMissingAudioWithoutChangingCharts()
    {
        using var temp = new Temp(); string chart = "TITLE:テスト\nWAVE:missing.ogg\nDEMOSTART:12.5\nCOURSE:Oni\n#START\n1111,\n#END";
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        await File.WriteAllTextAsync(Path.Combine(temp.Root, "a.tja"), chart, Encoding.GetEncoding(932));
        File.Copy(Path.Combine(temp.Root, "a.tja"), Path.Combine(temp.Root, "b.tja"));
        var songs = await SongLibraryService.ScanAsync(temp.Root);
        Assert.HasCount(2, songs); Assert.AreEqual("テスト", songs[0].Title); Assert.AreEqual(12.5, songs[0].DemoStart);
        Assert.HasCount(1, SongLibraryService.FindDuplicates(songs)); Assert.IsTrue(songs.All(s => s.Problems.Any(p => p.Contains("音源"))));
        Assert.AreEqual(chart, await File.ReadAllTextAsync(Path.Combine(temp.Root, "a.tja"), Encoding.GetEncoding(932)));
    }

    [TestMethod]
    public async Task LibraryRejectsAudioOutsideSongsAndBrokenChartMarkers()
    {
        using var temp = new Temp();
        await File.WriteAllTextAsync(Path.Combine(temp.Root, "a.tja"), "TITLE:A\nWAVE:../outside.ogg\n#START\n1,");
        var songs = await SongLibraryService.ScanAsync(temp.Root);
        Assert.HasCount(2, songs[0].Problems); Assert.IsNull(songs[0].AudioPath);
    }

    [TestMethod]
    public async Task HistoryCountsEventsAndIgnoresCorruptAndDuplicateIds()
    {
        using var temp = new Temp(); var installation = new TaikoDiveInstallation(temp.Root);
        string folder = Path.Combine(LauncherToolkitStore.Root(installation), "history"); Directory.CreateDirectory(folder);
        string json = JsonSerializer.Serialize(new PlayHistoryEntry { Id = "round-1", PlayedAtUtc = DateTime.UtcNow, SongTitle = "A", PlayerName = "P1" }, LauncherJsonContext.Default.PlayHistoryEntry);
        await File.WriteAllTextAsync(Path.Combine(folder, "a.json"), json); await File.WriteAllTextAsync(Path.Combine(folder, "duplicate.json"), json);
        await File.WriteAllTextAsync(Path.Combine(folder, "broken.json"), "{");
        Assert.HasCount(1, await LauncherToolkitStore.ReadHistoryAsync(installation));
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
    public void DiagnosticRedactionRemovesAddressesAndWindowsPaths()
    {
        string result = LauncherMaintenance.Redact("error at C:\\Users\\Alice\\build\\file.cs\nserver 192.168.1.2:22047 https://example.test/private");
        Assert.IsFalse(result.Contains("Alice")); Assert.IsFalse(result.Contains("192.168")); Assert.IsFalse(result.Contains("example.test"));
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
