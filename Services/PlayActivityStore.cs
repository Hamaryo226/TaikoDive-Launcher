using System.Text.Json;
using System.Text.Json.Serialization;
using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

internal static class PlayActivityStore
{
    private const int MaximumEntrySize = 16 * 1024;

    internal static DateTime GetHistoryStamp(string buildDirectory) => Directory.GetLastWriteTimeUtc(HistoryFolder(buildDirectory));

    private static string HistoryFolder(string buildDirectory) => Path.Combine(buildDirectory, "Info", "TaikoDiveLauncher", "history");

    internal static Task<IReadOnlyList<PlayActivityEntry>> LoadAsync(string buildDirectory, CancellationToken token = default) =>
        Task.Run<IReadOnlyList<PlayActivityEntry>>(() => Read(buildDirectory, token), token);

    private static IReadOnlyList<PlayActivityEntry> Read(string buildDirectory, CancellationToken token)
    {
        string folder = HistoryFolder(buildDirectory);
        if (!Directory.Exists(folder)) return [];

        var records = new Dictionary<string, PlayActivityEntry>(StringComparer.Ordinal);
        var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        foreach (string file in Directory.EnumerateFiles(folder, "*.json", options))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (input.Length > MaximumEntrySize) continue;
                var entry = JsonSerializer.Deserialize(input, PlayActivityJsonContext.Default.PlayActivityEntry);
                if (entry is not null && !string.IsNullOrWhiteSpace(entry.Id) && entry.PlayedAtUtc != default)
                    records.TryAdd(entry.Id, entry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // An unreadable entry must not hide the rest of the history.
            }
        }
        return records.Values.OrderByDescending(entry => entry.PlayedAtUtc).ToArray();
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PlayActivityEntry))]
internal partial class PlayActivityJsonContext : JsonSerializerContext;
