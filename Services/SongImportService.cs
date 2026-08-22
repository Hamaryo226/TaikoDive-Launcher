using System.IO.Compression;

namespace TaikoDiveLauncher.Services;

public sealed record SongGenre(string Name, string DirectoryPath);

public sealed record SongImportResult(bool Succeeded, string Message, string? DestinationPath = null)
{
    public static SongImportResult Success(string message, string destinationPath) => new(true, message, destinationPath);

    public static SongImportResult Failure(string message) => new(false, message);
}

public static class SongImportService
{
    private const int MaximumEntryCount = 10_000;
    private const long MaximumExpandedBytes = 4L * 1024 * 1024 * 1024;

    public static IReadOnlyList<SongGenre> LoadGenres(TaikoDiveInstallation installation)
    {
        if (!Directory.Exists(installation.SongsDirectory))
        {
            return [];
        }

        return Directory.EnumerateDirectories(installation.SongsDirectory)
            .Select(path => new SongGenre(Path.GetFileName(path), Path.GetFullPath(path)))
            .OrderBy(genre => genre.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static async Task<SongImportResult> ImportAsync(
        TaikoDiveInstallation installation,
        string zipPath,
        SongGenre genre,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(zipPath) || !string.Equals(Path.GetExtension(zipPath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            return SongImportResult.Failure("有効なZIPファイルを選択してください。");
        }

        string songsRoot = EnsureTrailingSeparator(Path.GetFullPath(installation.SongsDirectory));
        string genreDirectory = Path.GetFullPath(genre.DirectoryPath);
        if (!Directory.Exists(genreDirectory) ||
            !string.Equals(Path.GetDirectoryName(genreDirectory), songsRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return SongImportResult.Failure("保存先ジャンルがSongsフォルダー直下にありません。");
        }

        string temporaryDirectory = Path.Combine(genreDirectory, $".launcher-import-{Guid.NewGuid():N}");
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            List<ZipArchiveEntry> fileEntries = archive.Entries
                .Where(entry => !string.IsNullOrEmpty(entry.Name))
                .ToList();
            if (fileEntries.Count == 0)
            {
                return SongImportResult.Failure("ZIP内に楽曲ファイルがありません。");
            }

            if (fileEntries.Count > MaximumEntryCount || ExceedsExpandedSizeLimit(fileEntries))
            {
                return SongImportResult.Failure("ZIPの展開サイズまたはファイル数が上限を超えています。");
            }

            string? commonRoot = FindCommonRoot(fileEntries);
            string destinationName = SanitizeDirectoryName(commonRoot ?? Path.GetFileNameWithoutExtension(zipPath));
            if (string.IsNullOrWhiteSpace(destinationName))
            {
                return SongImportResult.Failure("楽曲フォルダー名を判定できませんでした。");
            }

            string destinationDirectory = Path.Combine(genreDirectory, destinationName);
            if (Directory.Exists(destinationDirectory) || File.Exists(destinationDirectory))
            {
                return SongImportResult.Failure($"「{destinationName}」は選択したジャンルに既に存在します。既存データは上書きしません。");
            }

            Directory.CreateDirectory(temporaryDirectory);
            string temporaryRoot = EnsureTrailingSeparator(Path.GetFullPath(temporaryDirectory));
            foreach (ZipArchiveEntry entry in fileEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relativePath = GetRelativeEntryPath(entry.FullName, commonRoot);
                string outputPath = Path.GetFullPath(Path.Combine(temporaryDirectory, relativePath));
                if (!outputPath.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return SongImportResult.Failure("ZIP内に不正なパスが含まれているため、展開を中止しました。");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                await using FileStream output = new(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await using Stream input = entry.Open();
                await input.CopyToAsync(output, cancellationToken);
            }

            if (!Directory.EnumerateFiles(temporaryDirectory, "*.tja", SearchOption.AllDirectories).Any())
            {
                return SongImportResult.Failure("ZIP内にTJA譜面が見つかりませんでした。");
            }

            Directory.Move(temporaryDirectory, destinationDirectory);
            return SongImportResult.Success($"「{destinationName}」を「{genre.Name}」へ追加しました。", destinationDirectory);
        }
        catch (InvalidDataException ex)
        {
            return SongImportResult.Failure($"ZIPを読み込めませんでした: {ex.Message}");
        }
        catch (IOException ex)
        {
            return SongImportResult.Failure($"楽曲を保存できませんでした: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return SongImportResult.Failure($"楽曲フォルダーへ書き込めませんでした: {ex.Message}");
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                try
                {
                    Directory.Delete(temporaryDirectory, recursive: true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static bool ExceedsExpandedSizeLimit(IEnumerable<ZipArchiveEntry> entries)
    {
        long total = 0;
        foreach (ZipArchiveEntry entry in entries)
        {
            if (entry.Length > MaximumExpandedBytes - total)
            {
                return true;
            }

            total += entry.Length;
        }

        return false;
    }

    private static string? FindCommonRoot(IReadOnlyList<ZipArchiveEntry> entries)
    {
        string? root = null;
        foreach (ZipArchiveEntry entry in entries)
        {
            string[] segments = SplitEntryPath(entry.FullName);
            if (segments.Length < 2)
            {
                return null;
            }

            if (root is null)
            {
                root = segments[0];
            }
            else if (!string.Equals(root, segments[0], StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return root;
    }

    private static string GetRelativeEntryPath(string fullName, string? commonRoot)
    {
        string normalizedPath = fullName.Replace('\\', '/');
        string[] segments = SplitEntryPath(normalizedPath);
        if (normalizedPath.StartsWith('/') ||
            Path.IsPathRooted(normalizedPath.Replace('/', Path.DirectorySeparatorChar)) ||
            segments.Any(segment => segment == ".." || segment.Contains(':')))
        {
            throw new InvalidDataException("ZIP entry contains an absolute or parent path segment.");
        }

        if (commonRoot is not null)
        {
            segments = segments[1..];
        }

        if (segments.Length == 0 || segments.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException("ZIP entry path is empty.");
        }

        return Path.Combine(segments);
    }

    private static string[] SplitEntryPath(string path) => path
        .Replace('\\', '/')
        .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string SanitizeDirectoryName(string value)
    {
        string sanitized = value.Trim().TrimEnd('.', ' ');
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(invalid, '_');
        }

        return sanitized;
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}
