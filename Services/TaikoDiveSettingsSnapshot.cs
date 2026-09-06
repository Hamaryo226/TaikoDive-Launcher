namespace TaikoDiveLauncher.Services;

internal sealed record TaikoDiveSettingsSnapshot(
    string SettingsPath,
    string BackupPath);

internal static class TaikoDiveSettingsPreserver
{
    private const string BackupFileName = "Setting.json.preserve";

    public static async Task<TaikoDiveSettingsSnapshot?> CaptureAsync(
        string settingsPath,
        string backupDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(settingsPath))
        {
            return null;
        }

        Directory.CreateDirectory(backupDirectory);
        string backupPath = Path.Combine(backupDirectory, BackupFileName);
        await CopyAtomicAsync(settingsPath, backupPath, cancellationToken).ConfigureAwait(false);
        return new TaikoDiveSettingsSnapshot(
            Path.GetFullPath(settingsPath),
            Path.GetFullPath(backupPath));
    }

    public static Task RestoreAsync(
        TaikoDiveSettingsSnapshot? snapshot,
        CancellationToken cancellationToken = default)
    {
        return snapshot is null
            ? Task.CompletedTask
            : RestoreAsync(snapshot.SettingsPath, snapshot.BackupPath, cancellationToken);
    }

    public static async Task RestoreAsync(
        string settingsPath,
        string backupPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("退避したSetting.jsonが見つかりません。", backupPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(settingsPath))!);
        await CopyAtomicAsync(backupPath, settingsPath, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CopyAtomicAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        string fullDestinationPath = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullDestinationPath)!);
        string pendingPath = fullDestinationPath + $".{Guid.NewGuid():N}.pending";
        try
        {
            await using (FileStream source = new(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (FileStream destination = new(
                pendingPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(pendingPath, fullDestinationPath, overwrite: true);
        }
        finally
        {
            TryDelete(pendingPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
