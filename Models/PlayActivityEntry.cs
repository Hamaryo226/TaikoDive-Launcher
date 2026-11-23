namespace TaikoDiveLauncher.Models;

internal sealed record PlayActivityEntry(string Id, DateTimeOffset PlayedAtUtc, string PlayerName, string SongTitle, int Course, int Score);

internal sealed record PlayActivityPeriod(DateOnly Start, DateOnly End, IReadOnlyList<PlayActivityEntry> Plays, int CumulativeCount = 0)
{
    public int Count => Plays.Count;
}

internal sealed record PlayActivityChart(IReadOnlyList<PlayActivityPeriod> Days, IReadOnlyList<PlayActivityPeriod> Weeks);
