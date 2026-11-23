using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

internal static class PlayActivity
{
    internal const int DaysShown = 365;
    internal const int WeeksShown = 53;

    internal static PlayActivityChart Build(IEnumerable<PlayActivityEntry> entries, DateOnly today, TimeZoneInfo zone, string? player = null)
    {
        DateOnly firstDay = today.AddDays(1 - DaysShown);
        DateOnly firstWeek = Monday(today).AddDays(7 * (1 - WeeksShown));
        Dictionary<DateOnly, List<PlayActivityEntry>> days = [];
        Dictionary<DateOnly, List<PlayActivityEntry>> weeks = [];
        int previousPlays = 0;
        foreach (PlayActivityEntry entry in entries)
        {
            if (player is not null && !string.Equals(entry.PlayerName, player, StringComparison.OrdinalIgnoreCase)) continue;
            DateOnly day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(entry.PlayedAtUtc, zone).DateTime);
            if (day > today) continue;
            if (day < firstWeek) previousPlays++;
            else Add(weeks, Monday(day), entry);
            if (day >= firstDay) Add(days, day, entry);
        }

        PlayActivityPeriod[] daily = new PlayActivityPeriod[DaysShown];
        for (int index = 0; index < daily.Length; index++)
        {
            DateOnly day = firstDay.AddDays(index);
            daily[index] = new(day, day, SortedPlays(days, day));
        }

        PlayActivityPeriod[] weekly = new PlayActivityPeriod[WeeksShown];
        for (int index = 0; index < weekly.Length; index++)
        {
            DateOnly monday = firstWeek.AddDays(index * 7);
            var plays = SortedPlays(weeks, monday);
            previousPlays += plays.Count;
            weekly[index] = new(monday, index == weekly.Length - 1 ? today : monday.AddDays(6), plays, previousPlays);
        }
        return new(daily, weekly);
    }

    internal static DateOnly Monday(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    private static void Add(Dictionary<DateOnly, List<PlayActivityEntry>> groups, DateOnly day, PlayActivityEntry entry)
    {
        if (!groups.TryGetValue(day, out var group)) groups[day] = group = [];
        group.Add(entry);
    }

    private static IReadOnlyList<PlayActivityEntry> SortedPlays(Dictionary<DateOnly, List<PlayActivityEntry>> groups, DateOnly day) =>
        groups.TryGetValue(day, out var group) ? group.OrderByDescending(play => play.PlayedAtUtc).ToArray() : [];
}
