namespace TaikoDiveLauncher.Services;

public sealed class TaikoDiveInstallation
{
    public TaikoDiveInstallation(string buildDirectory)
    {
        BuildDirectory = Path.GetFullPath(buildDirectory);
    }

    public string BuildDirectory { get; }

    public string ExecutablePath => Path.Combine(BuildDirectory, "TaikoDive.exe");

    public string GameSettingsPath => Path.Combine(BuildDirectory, "Setting.json");

    public string UserProfilePath => Path.Combine(BuildDirectory, "Info", "User.ini");

    public string CharacterDirectory => Path.Combine(BuildDirectory, "Info", "Chara");

    public string NamePlateDirectory => Path.Combine(BuildDirectory, "Texture", "NamePlate", "Plates");

    public string ScoreDirectory => Path.Combine(BuildDirectory, "Info", "ScoreData");

    public string SongsDirectory => Path.Combine(BuildDirectory, "Songs");

    public bool IsValid => File.Exists(ExecutablePath);

    public static string ApplicationDirectory => ResolveApplicationDirectory(
        Environment.ProcessPath,
        AppContext.BaseDirectory);

    public static TaikoDiveInstallation? FromSelectedDirectory(string? selectedDirectory)
    {
        if (string.IsNullOrWhiteSpace(selectedDirectory))
        {
            return null;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(selectedDirectory);
        }
        catch
        {
            return null;
        }

        foreach (string candidate in new[] { fullPath, Path.Combine(fullPath, "build") })
        {
            TaikoDiveInstallation installation = new(candidate);
            if (installation.IsValid)
            {
                return installation;
            }
        }

        return null;
    }

    public static TaikoDiveInstallation? FromApplicationDirectory()
    {
        string? developmentDirectory = Environment.GetEnvironmentVariable("TAIKODIVE_LAUNCHER_DEV_DIRECTORY");
        return FromSelectedDirectory(developmentDirectory)
            ?? FromSelectedDirectory(ApplicationDirectory)
            ?? FromSelectedDirectory(AppContext.BaseDirectory);
    }

    internal static string ResolveApplicationDirectory(string? processPath, string fallbackDirectory)
    {
        if (!string.IsNullOrWhiteSpace(processPath)
            && !string.Equals(Path.GetFileName(processPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string? processDirectory = Path.GetDirectoryName(Path.GetFullPath(processPath));
                if (!string.IsNullOrWhiteSpace(processDirectory))
                {
                    return processDirectory;
                }
            }
            catch
            {
                // Fall back to AppContext for unusual hosts or malformed process paths.
            }
        }

        return Path.GetFullPath(fallbackDirectory);
    }
}
