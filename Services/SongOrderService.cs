using System.Text;
using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

public sealed record OrderedSong(string Title, string RelativePath, string FilePath)
{
    public override string ToString() => Title;
}

public static class SongOrderService
{
    public const string OrderFileName = ".taikodive-order";

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private static readonly Encoding ShiftJis = CreateShiftJisEncoding();
    private static readonly SemaphoreSlim SaveLock = new(1, 1);

    public static IReadOnlyList<OrderedSong> LoadSongs(SongGenre genre)
    {
        string genreDirectory = Path.GetFullPath(genre.DirectoryPath);
        if (!Directory.Exists(genreDirectory))
        {
            return [];
        }

        List<OrderedSong> songs = Directory.EnumerateFiles(genreDirectory, "*.tja", SearchOption.AllDirectories)
            .Select(path => CreateSong(genreDirectory, path))
            .ToList();
        Dictionary<string, int> savedOrder = LoadSavedOrder(genreDirectory);
        if (savedOrder.Count == 0)
        {
            return songs;
        }

        return songs
            .Select((song, originalIndex) => new
            {
                Song = song,
                OriginalIndex = originalIndex,
                SavedIndex = savedOrder.GetValueOrDefault(song.RelativePath, int.MaxValue),
            })
            .OrderBy(item => item.SavedIndex)
            .ThenBy(item => item.OriginalIndex)
            .Select(item => item.Song)
            .ToList();
    }

    public static Task<OperationResult> SaveOrderAsync(
        TaikoDiveInstallation installation,
        SongGenre genre,
        IReadOnlyList<OrderedSong> songs,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (GameProcessService.IsRunning())
            {
                return OperationResult.Failure(
                    "TaikoDiveの実行中は曲順を変更できません。ゲームを終了してから再試行してください。");
            }

            return await SaveOrderWithoutProcessCheckAsync(
                installation,
                genre,
                songs,
                cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    internal static async Task<OperationResult> SaveOrderWithoutProcessCheckAsync(
        TaikoDiveInstallation installation,
        SongGenre genre,
        IReadOnlyList<OrderedSong> songs,
        CancellationToken cancellationToken = default)
    {
        await SaveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string songsDirectory = Path.GetFullPath(installation.SongsDirectory);
            string genreDirectory = Path.GetFullPath(genre.DirectoryPath);
            if (!Directory.Exists(genreDirectory)
                || !string.Equals(
                    Path.GetDirectoryName(genreDirectory),
                    songsDirectory.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult.Failure("選択したジャンルが現在のSongsフォルダー直下にありません。");
            }

            string[] currentPaths = Directory.EnumerateFiles(genreDirectory, "*.tja", SearchOption.AllDirectories)
                .Select(path => NormalizeRelativePath(Path.GetRelativePath(genreDirectory, path)))
                .ToArray();
            string[] requestedPaths = songs.Select(song => NormalizeRelativePath(song.RelativePath)).ToArray();
            if (requestedPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != requestedPaths.Length
                || !new HashSet<string>(currentPaths, StringComparer.OrdinalIgnoreCase).SetEquals(requestedPaths))
            {
                return OperationResult.Failure("楽曲一覧が変更されています。再読み込みしてから並べ替えてください。");
            }

            string content = requestedPaths.Length == 0
                ? string.Empty
                : string.Join(Environment.NewLine, requestedPaths) + Environment.NewLine;
            cancellationToken.ThrowIfCancellationRequested();
            await FilePersistence.WriteTextAtomicAsync(
                Path.Combine(genreDirectory, OrderFileName),
                content,
                new UTF8Encoding(false),
                createBackup: true).ConfigureAwait(false);
            return OperationResult.Success($"「{genre.Name}」の曲順を{requestedPaths.Length}曲分保存しました。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException ex)
        {
            return OperationResult.Failure($"曲順を保存できませんでした: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult.Failure($"曲順ファイルへ書き込めませんでした: {ex.Message}");
        }
        finally
        {
            SaveLock.Release();
        }
    }

    private static OrderedSong CreateSong(string genreDirectory, string path)
    {
        string relativePath = NormalizeRelativePath(Path.GetRelativePath(genreDirectory, path));
        string title = ReadTitle(path);
        if (string.IsNullOrWhiteSpace(title))
        {
            title = Path.GetFileNameWithoutExtension(path);
        }

        return new OrderedSong(title, relativePath, Path.GetFullPath(path));
    }

    private static Dictionary<string, int> LoadSavedOrder(string genreDirectory)
    {
        string orderPath = Path.Combine(genreDirectory, OrderFileName);
        if (!File.Exists(orderPath))
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, int> order = new(StringComparer.OrdinalIgnoreCase);
        int index = 0;
        foreach (string line in File.ReadLines(orderPath, Encoding.UTF8))
        {
            string relativePath = NormalizeRelativePath(line.Trim());
            if (relativePath.Length > 0 && !order.ContainsKey(relativePath))
            {
                order.Add(relativePath, index++);
            }
        }

        return order;
    }

    private static string ReadTitle(string path)
    {
        try
        {
            return ReadTitle(path, StrictUtf8);
        }
        catch (DecoderFallbackException)
        {
            return ReadTitle(path, ShiftJis);
        }
    }

    private static string ReadTitle(string path, Encoding encoding)
    {
        string? title = null;
        using StreamReader reader = new(path, encoding, detectEncodingFromByteOrderMarks: true);
        for (int lineNumber = 0; lineNumber < 512; lineNumber++)
        {
            string? line = reader.ReadLine();
            if (line is null)
            {
                break;
            }

            string trimmed = line.TrimStart('\uFEFF', ' ', '\t');
            if (trimmed.StartsWith("TITLEJA:", StringComparison.OrdinalIgnoreCase))
            {
                string japaneseTitle = trimmed["TITLEJA:".Length..].Trim();
                if (japaneseTitle.Length > 0)
                {
                    return japaneseTitle;
                }
            }
            else if (trimmed.StartsWith("TITLE:", StringComparison.OrdinalIgnoreCase))
            {
                title = trimmed["TITLE:".Length..].Trim();
            }
            else if (trimmed.StartsWith("#START", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        return title ?? string.Empty;
    }

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/');

    private static Encoding CreateShiftJisEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }
}
