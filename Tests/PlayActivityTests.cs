using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Tests;

[TestClass]
public sealed class PlayActivityTests
{
    [TestMethod]
    public void DailyGroupsActualPlaysInLocalCalendarDaysAndFiltersPlayer()
    {
        var japan = TimeZoneInfo.CreateCustomTimeZone("Test JST", TimeSpan.FromHours(9), "Test", "Test");
        var chart = PlayActivity.Build([
            Play("a", "Alice", "2026-11-20T15:00:00Z"), Play("b", "Alice", "2026-11-21T02:00:00Z"),
            Play("c", "Bob", "2026-11-20T15:00:00Z"), Play("future", "Alice", "2026-11-23T00:00:00Z"),
        ], new DateOnly(2026, 11, 22), japan, "Alice");

        Assert.HasCount(365, chart.Days);
        Assert.AreEqual(2, chart.Days.Single(day => day.Start == new DateOnly(2026, 11, 21)).Count);
        Assert.AreEqual(0, chart.Days[^1].Count);
        Assert.HasCount(2, chart.Days[^2].Plays);
    }

    [TestMethod]
    public void WeeklyStartsMondayAndLifetimeIncludesPlaysBeforeTheVisibleYear()
    {
        var chart = PlayActivity.Build([
            Play("old", "Alice", "2024-01-01T00:00:00Z"),
            Play("sun", "Alice", "2026-11-15T23:59:59Z"), Play("mon", "Alice", "2026-11-16T00:00:00Z"),
            Play("today", "Alice", "2026-11-22T00:00:00Z"),
        ], new DateOnly(2026, 11, 22), TimeZoneInfo.Utc);

        Assert.HasCount(53, chart.Weeks);
        Assert.AreEqual(DayOfWeek.Monday, chart.Weeks[0].Start.DayOfWeek);
        Assert.AreEqual(new DateOnly(2026, 11, 16), chart.Weeks[^1].Start);
        Assert.AreEqual(1, chart.Weeks[^2].Count);
        Assert.AreEqual(2, chart.Weeks[^1].Count);
        Assert.AreEqual(1, chart.Weeks[0].CumulativeCount);
        Assert.AreEqual(4, chart.Weeks[^1].CumulativeCount);
    }

    [TestMethod]
    public async Task ReadsBridgeFilesOnceAndIgnoresCorruptRecordsAndScoreFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string history = Path.Combine(root, "Info", "TaikoDiveLauncher", "history");
        Directory.CreateDirectory(history);
        try
        {
            const string json = """{"id":"a-0","playedAtUtc":"2026-11-20T16:00:00Z","playerName":"Alice","songTitle":"曲","course":3,"score":120000}""";
            await File.WriteAllTextAsync(Path.Combine(history, "one.json"), json);
            await File.WriteAllTextAsync(Path.Combine(history, "copy.json"), json);
            await File.WriteAllTextAsync(Path.Combine(history, "broken.json"), "{");
            await File.WriteAllTextAsync(Path.Combine(history, "partial.json"), """{"id":"missing-date"}""");
            await File.WriteAllTextAsync(Path.Combine(history, "Score.dat"), json);
            await File.WriteAllTextAsync(Path.Combine(history, "temporary.json.tmp"), json.Replace("a-0", "a-1"));

            var result = await PlayActivityStore.LoadAsync(root);
            Assert.HasCount(1, result);
            Assert.AreEqual("曲", result[0].SongTitle);
            Assert.AreEqual(120000, result[0].Score);
            Assert.AreEqual(TimeSpan.Zero, result[0].PlayedAtUtc.Offset);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static PlayActivityEntry Play(string id, string name, string at) =>
        new(id, DateTimeOffset.Parse(at, System.Globalization.CultureInfo.InvariantCulture), name, "曲", 3, 1000);
}
