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

    public bool IsValid => File.Exists(ExecutablePath);

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

    public static TaikoDiveInstallation? Discover()
    {
        HashSet<string> candidates = new(StringComparer.OrdinalIgnoreCase)
        {
            Environment.CurrentDirectory,
            AppContext.BaseDirectory,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "GitHub",
                "TaikoDive"),
        };

        AddAncestors(candidates, Environment.CurrentDirectory);
        AddAncestors(candidates, AppContext.BaseDirectory);

        foreach (string candidate in candidates)
        {
            foreach (string probe in new[]
            {
                candidate,
                Path.Combine(candidate, "build"),
                Path.Combine(candidate, "TaikoDive"),
                Path.Combine(candidate, "TaikoDive", "build"),
            })
            {
                TaikoDiveInstallation? installation = FromSelectedDirectory(probe);
                if (installation is not null)
                {
                    return installation;
                }
            }
        }

        return null;
    }

    private static void AddAncestors(ISet<string> candidates, string path)
    {
        DirectoryInfo? directory;
        try
        {
            directory = new DirectoryInfo(path);
        }
        catch
        {
            return;
        }

        for (int depth = 0; directory is not null && depth < 6; depth++)
        {
            candidates.Add(directory.FullName);
            directory = directory.Parent;
        }
    }
}
