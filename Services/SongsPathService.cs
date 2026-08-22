using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

public sealed record SongsPathState(string EffectivePath, bool IsRedirected, bool CanRestore);

public static class SongsPathService
{
    private static readonly HashSet<string> RequiredGenreFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "box.def",
        "CenterText.apt",
    };

    public static SongsPathState GetState(TaikoDiveInstallation installation)
    {
        string songsPath = installation.SongsDirectory;
        bool isRedirected = IsDirectoryLink(songsPath);
        string effectivePath = isRedirected
            ? ResolveLinkTarget(songsPath) ?? songsPath
            : songsPath;
        return new SongsPathState(
            effectivePath,
            isRedirected,
            isRedirected && Directory.Exists(GetBackupPath(installation)));
    }

    public static async Task<OperationResult> ChangeAsync(
        TaikoDiveInstallation installation,
        string targetDirectory,
        CancellationToken cancellationToken = default)
    {
        if (GameProcessService.IsRunning())
        {
            return OperationResult.Failure("TaikoDiveの実行中はSongsフォルダーを変更できません。ゲームを終了してから再試行してください。");
        }

        return await ChangeWithoutProcessCheckAsync(installation, targetDirectory, cancellationToken);
    }

    internal static async Task<OperationResult> ChangeWithoutProcessCheckAsync(
        TaikoDiveInstallation installation,
        string targetDirectory,
        CancellationToken cancellationToken = default)
    {

        string targetPath;
        try
        {
            targetPath = NormalizeDirectory(targetDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return OperationResult.Failure($"Songsフォルダーのパスが正しくありません: {ex.Message}");
        }

        if (!Directory.Exists(targetPath))
        {
            return OperationResult.Failure("選択したSongsフォルダーが見つかりません。");
        }

        string songsPath = NormalizeDirectory(installation.SongsDirectory);
        string backupPath = NormalizeDirectory(GetBackupPath(installation));
        SongsPathState currentState = GetState(installation);
        if (PathsEqual(currentState.EffectivePath, targetPath))
        {
            return OperationResult.Success("Songsフォルダーはすでに選択した場所を使用しています。");
        }

        if (IsSameOrChildPath(targetPath, songsPath) || IsSameOrChildPath(targetPath, backupPath))
        {
            return OperationResult.Failure("TaikoDive側のSongsフォルダーまたはそのバックアップ内は選択できません。");
        }

        return await Task.Run(
            () => ChangeCore(installation, targetPath, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<OperationResult> RestoreAsync(
        TaikoDiveInstallation installation,
        CancellationToken cancellationToken = default)
    {
        if (GameProcessService.IsRunning())
        {
            return OperationResult.Failure("TaikoDiveの実行中はSongsフォルダーを元に戻せません。ゲームを終了してから再試行してください。");
        }

        return await RestoreWithoutProcessCheckAsync(installation, cancellationToken);
    }

    internal static async Task<OperationResult> RestoreWithoutProcessCheckAsync(
        TaikoDiveInstallation installation,
        CancellationToken cancellationToken = default)
    {

        return await Task.Run(
            () => RestoreCore(installation, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    internal static int CopyRequiredAssets(string sourceSongsDirectory, string targetSongsDirectory)
    {
        int copiedCount = 0;
        foreach (string sourceGenre in Directory.EnumerateDirectories(sourceSongsDirectory))
        {
            string? genreName = Path.GetFileName(sourceGenre);
            if (string.IsNullOrWhiteSpace(genreName))
            {
                continue;
            }

            string imageDirectory = Path.Combine(sourceGenre, "Image");
            string[] assetFiles = Directory.EnumerateFiles(sourceGenre, "*", SearchOption.TopDirectoryOnly)
                .Where(path => RequiredGenreFiles.Contains(Path.GetFileName(path)))
                .ToArray();
            if (assetFiles.Length == 0 && !Directory.Exists(imageDirectory))
            {
                continue;
            }

            string targetGenre = Path.Combine(targetSongsDirectory, genreName);
            Directory.CreateDirectory(targetGenre);
            foreach (string sourceFile in assetFiles)
            {
                copiedCount += CopyIfMissing(sourceFile, Path.Combine(targetGenre, Path.GetFileName(sourceFile)));
            }

            if (Directory.Exists(imageDirectory))
            {
                copiedCount += CopyDirectoryIfMissing(imageDirectory, Path.Combine(targetGenre, "Image"));
            }
        }

        return copiedCount;
    }

    private static OperationResult ChangeCore(
        TaikoDiveInstallation installation,
        string targetPath,
        CancellationToken cancellationToken)
    {
        string songsPath = installation.SongsDirectory;
        string backupPath = GetBackupPath(installation);
        bool currentIsLink = IsDirectoryLink(songsPath);
        string? previousTarget = currentIsLink ? ResolveLinkTarget(songsPath) : null;
        bool movedOriginal = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string assetSource;
            if (Directory.Exists(backupPath) && !IsDirectoryLink(backupPath))
            {
                assetSource = backupPath;
            }
            else if (Directory.Exists(songsPath))
            {
                assetSource = songsPath;
            }
            else
            {
                return OperationResult.Failure("TaikoDiveのSongsフォルダーが見つかりません。");
            }

            int copiedAssets = CopyRequiredAssets(assetSource, targetPath);
            cancellationToken.ThrowIfCancellationRequested();

            if (currentIsLink)
            {
                Directory.Delete(songsPath);
            }
            else
            {
                if (Directory.Exists(backupPath))
                {
                    return OperationResult.Failure($"元のSongsフォルダー用バックアップがすでに存在します: {backupPath}");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                Directory.Move(songsPath, backupPath);
                movedOriginal = true;
            }

            Directory.CreateSymbolicLink(songsPath, targetPath);
            return OperationResult.Success(
                $"Songsフォルダーを「{targetPath}」へ変更しました。TaikoDive用アセットを{copiedAssets}ファイル補完しました（既存ファイルは上書きしていません）。");
        }
        catch (OperationCanceledException)
        {
            TryRestorePreviousPath(songsPath, backupPath, movedOriginal, previousTarget);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            TryRestorePreviousPath(songsPath, backupPath, movedOriginal, previousTarget);
            return OperationResult.Failure($"Songsフォルダーを変更できませんでした: {ex.Message}");
        }
    }

    private static OperationResult RestoreCore(
        TaikoDiveInstallation installation,
        CancellationToken cancellationToken)
    {
        string songsPath = installation.SongsDirectory;
        string backupPath = GetBackupPath(installation);
        if (!IsDirectoryLink(songsPath) || !Directory.Exists(backupPath) || IsDirectoryLink(backupPath))
        {
            return OperationResult.Failure("元に戻せるTaikoDiveのSongsフォルダーがありません。");
        }

        string? previousTarget = ResolveLinkTarget(songsPath);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Delete(songsPath);
            Directory.Move(backupPath, songsPath);
            return OperationResult.Success("TaikoDive標準のSongsフォルダーへ戻しました。外部Songs内へ補完したアセットは削除していません。");
        }
        catch (OperationCanceledException)
        {
            TryRecreateLink(songsPath, previousTarget);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            TryRecreateLink(songsPath, previousTarget);
            return OperationResult.Failure($"TaikoDive標準のSongsフォルダーへ戻せませんでした: {ex.Message}");
        }
    }

    private static int CopyDirectoryIfMissing(string sourceDirectory, string targetDirectory)
    {
        int copiedCount = 0;
        foreach (string sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
            copiedCount += CopyIfMissing(sourceFile, Path.Combine(targetDirectory, relativePath));
        }

        return copiedCount;
    }

    private static int CopyIfMissing(string sourceFile, string targetFile)
    {
        if (File.Exists(targetFile) || Directory.Exists(targetFile))
        {
            return 0;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
        File.Copy(sourceFile, targetFile, overwrite: false);
        return 1;
    }

    private static bool IsDirectoryLink(string path)
    {
        try
        {
            return Directory.Exists(path)
                && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? ResolveLinkTarget(string path)
    {
        try
        {
            return new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void TryRestorePreviousPath(
        string songsPath,
        string backupPath,
        bool movedOriginal,
        string? previousTarget)
    {
        try
        {
            if (IsDirectoryLink(songsPath))
            {
                Directory.Delete(songsPath);
            }

            if (movedOriginal && Directory.Exists(backupPath) && !Directory.Exists(songsPath))
            {
                Directory.Move(backupPath, songsPath);
            }
            else
            {
                TryRecreateLink(songsPath, previousTarget);
            }
        }
        catch
        {
            // The original exception is more useful. A remaining backup is never deleted.
        }
    }

    private static void TryRecreateLink(string songsPath, string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath) || Directory.Exists(songsPath))
        {
            return;
        }

        try
        {
            Directory.CreateSymbolicLink(songsPath, targetPath);
        }
        catch
        {
        }
    }

    private static string GetBackupPath(TaikoDiveInstallation installation) =>
        Path.Combine(installation.BuildDirectory, "Info", "TaikoDiveLauncher", "Songs.original");

    private static string NormalizeDirectory(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool PathsEqual(string first, string second) =>
        string.Equals(NormalizeDirectory(first), NormalizeDirectory(second), StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrChildPath(string candidate, string parent)
    {
        string normalizedCandidate = NormalizeDirectory(candidate);
        string normalizedParent = NormalizeDirectory(parent);
        return PathsEqual(normalizedCandidate, normalizedParent)
            || normalizedCandidate.StartsWith(
                normalizedParent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }
}
