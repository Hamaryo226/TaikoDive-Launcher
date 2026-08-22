namespace TaikoDiveLauncher.Services;

public sealed record TaikoNautsInstallation(string ExecutablePath, string SongsDirectory);

public static class TaikoNautsDiscoveryService
{
    public static Task<IReadOnlyList<TaikoNautsInstallation>> FindInstallationsAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run(() => FindInstallations(GetDefaultSearchRoots(), cancellationToken), cancellationToken);

    public static IReadOnlyList<TaikoNautsInstallation> FindInstallations(
        IEnumerable<string> searchRoots,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, TaikoNautsInstallation> installations = new(StringComparer.OrdinalIgnoreCase);
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
            MatchCasing = MatchCasing.CaseInsensitive,
            MaxRecursionDepth = 8,
            ReturnSpecialDirectories = false,
        };

        foreach (string root in searchRoots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (string executablePath in Directory.EnumerateFiles(root, "TaikoNauts.exe", options))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string? installationDirectory = Path.GetDirectoryName(executablePath);
                    if (installationDirectory is null)
                    {
                        continue;
                    }

                    string songsDirectory = Path.Combine(installationDirectory, "Songs");
                    if (Directory.Exists(songsDirectory))
                    {
                        string fullExecutablePath = Path.GetFullPath(executablePath);
                        installations.TryAdd(
                            fullExecutablePath,
                            new TaikoNautsInstallation(fullExecutablePath, Path.GetFullPath(songsDirectory)));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException)
            {
                // Continue with the other standard locations.
            }
        }

        return installations.Values
            .OrderBy(installation => installation.ExecutablePath, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> GetDefaultSearchRoots()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return
        [
            TaikoDiveInstallation.ApplicationDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(userProfile, "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        ];
    }
}
