using System.Text;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Tests;

[TestClass]
public sealed class SongOrderHeaderTests
{
    [TestMethod]
    [DataRow("utf8", "TITLE:")]
    [DataRow("sjis", "TITLE:日本語")]
    [DataRow("utf16", "//")]
    [DataRow("utf32", "TITLEJA:")]
    public void OversizedHeaderStopsWithoutAllocatingTheWholeLine(string encodingName, string prefix)
    {
        Encoding encoding = GetEncoding(encodingName);
        using ChartFolder folder = new();
        File.WriteAllText(folder.ChartPath, "TITLE:Warmup\n#START", encoding);
        _ = SongOrderService.LoadSongs(folder.Genre);
        // Generate outside the measured region; large enough to expose ReadLine allocation,
        // but bounded so the regression itself cannot exhaust the test host.
        File.WriteAllText(folder.ChartPath, prefix + new string('x', 1024 * 1024), encoding);
        long before = GC.GetAllocatedBytesForCurrentThread();
        OrderedSong song = SongOrderService.LoadSongs(folder.Genre).Single();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.IsTrue(allocated < 256 * 1024, $"Header allocated {allocated} bytes.");
        Assert.AreEqual("chart", song.Title);
    }

    [TestMethod]
    [DataRow("utf8", "\n")]
    [DataRow("sjis", "\r\n")]
    [DataRow("utf16", "\r")]
    [DataRow("utf32", "\n")]
    public void NormalHeaderPreservesJapaneseTitleAndLineEndings(string encodingName, string newline)
    {
        using ChartFolder folder = new();
        File.WriteAllText(folder.ChartPath, $"TITLE:English{newline} \ttitleja: 日本語 {newline}#START", GetEncoding(encodingName));
        Assert.AreEqual("日本語", SongOrderService.LoadSongs(folder.Genre).Single().Title);
    }

    [TestMethod]
    [DataRow(16383, "")]
    [DataRow(16384, "")]
    [DataRow(16384, "\r\n")]
    [DataRow(16385, "")]
    [DataRow(16385, "\nTITLEJA:Must not be reached")]
    public void HeaderLengthBoundaryNeverAcceptsTruncatedTitles(int length, string suffix)
    {
        using ChartFolder folder = new();
        string value = new('x', length - "TITLE:".Length);
        File.WriteAllText(folder.ChartPath, "TITLE:" + value + suffix, new UTF8Encoding(true));
        Assert.AreEqual(length <= 16384 ? value : "chart", SongOrderService.LoadSongs(folder.Genre).Single().Title);
    }

    [TestMethod]
    [DataRow("TITLEJA:")]
    [DataRow("//")]
    public void OversizedLaterLineKeepsLastCompleteTitle(string prefix)
    {
        using ChartFolder folder = new();
        File.WriteAllText(folder.ChartPath, "TITLE:First\rTITLE:Last\r" + prefix + new string('x', 16385) + "\rTITLEJA:Not reached");
        Assert.AreEqual("Last", SongOrderService.LoadSongs(folder.Genre).Single().Title);
    }

    [TestMethod]
    [DataRow(511, "At limit")]
    [DataRow(512, "chart")]
    public void HeaderStillStopsAfter512Lines(int blankLines, string expected)
    {
        using ChartFolder folder = new();
        File.WriteAllText(folder.ChartPath, new string('\n', blankLines) + "TITLE:At limit");
        Assert.AreEqual(expected, SongOrderService.LoadSongs(folder.Genre).Single().Title);
    }

    [TestMethod]
    public void StartMarkerStillStopsBeforeChartData()
    {
        using ChartFolder folder = new();
        File.WriteAllText(folder.ChartPath, "TITLE:Header\n#START\nTITLEJA:Not a header\n" + new string('1', 1024 * 1024));
        Assert.AreEqual("Header", SongOrderService.LoadSongs(folder.Genre).Single().Title);
    }

    private static Encoding GetEncoding(string name)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return name switch
        {
            "sjis" => Encoding.GetEncoding(932),
            "utf16" => Encoding.Unicode,
            "utf32" => Encoding.UTF32,
            _ => new UTF8Encoding(false),
        };
    }

    private sealed class ChartFolder : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "SongOrderHeader-" + Guid.NewGuid().ToString("N"));
        public string ChartPath => Path.Combine(directory, "chart.tja");
        public SongGenre Genre => new("Test", directory);
        public ChartFolder() => Directory.CreateDirectory(directory);
        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}