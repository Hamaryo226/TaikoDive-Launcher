using System.Text;

namespace TaikoDiveLauncher.Services;

internal static class FilePersistence
{
    public static async Task WriteTextAtomicAsync(
        string path,
        string content,
        Encoding encoding,
        bool createBackup = true)
    {
        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("保存先フォルダーを特定できません。");
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = path + ".launcher.tmp";
        string backupPath = path + ".launcher.bak";

        await File.WriteAllTextAsync(temporaryPath, content, encoding).ConfigureAwait(false);

        try
        {
            if (createBackup && File.Exists(path))
            {
                File.Copy(path, backupPath, overwrite: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
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
        catch
        {
            // The original file is untouched. A stale temporary file is safe to leave behind.
        }
    }
}
