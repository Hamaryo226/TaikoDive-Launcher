using System.Text.Json;

namespace TaikoDiveLauncher.Services;

public sealed record CharacterPreviewData(IReadOnlyList<string> Frames, TimeSpan FrameInterval, string SourceDirectory);

public static class CharacterPreviewService
{
    private const string DefaultCharacterRoot = @"Info\Chara\<Type>";
    private const string DefaultNormalLoop = @"result_loop|Common\Normal_loop|songselect_loop|entry_loop";
    private const int MaximumPreviewFrames = 30;

    public static CharacterPreviewData? Load(TaikoDiveInstallation installation, string characterType)
    {
        Dictionary<string, string> paths = ReadPathMap(Path.Combine(installation.BuildDirectory, "Info", "CharaPath.ini"));
        string characterRoot = ResolvePath(
            installation.BuildDirectory,
            paths.GetValueOrDefault("Chara_Root", DefaultCharacterRoot).Replace("<Type>", characterType, StringComparison.OrdinalIgnoreCase));

        string candidates = paths.GetValueOrDefault("Common_NormalLoop", DefaultNormalLoop);
        string? animationDirectory = candidates
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(candidate => candidate.StartsWith('\\') || candidate.StartsWith('/')
                ? ResolvePath(installation.BuildDirectory, candidate.TrimStart('\\', '/'))
                : Path.Combine(characterRoot, candidate))
            .FirstOrDefault(Directory.Exists);

        if (animationDirectory is null)
        {
            return null;
        }

        List<string> allFrames = [];
        for (int index = 0; ; index++)
        {
            string framePath = Path.Combine(animationDirectory, $"{index}.png");
            if (!File.Exists(framePath))
            {
                break;
            }

            allFrames.Add(framePath);
        }

        if (allFrames.Count == 0)
        {
            return null;
        }

        int previewCount = Math.Min(MaximumPreviewFrames, allFrames.Count);
        List<string> previewFrames = new(previewCount);
        for (int index = 0; index < previewCount; index++)
        {
            int sourceIndex = (int)Math.Floor(index * allFrames.Count / (double)previewCount);
            previewFrames.Add(allFrames[sourceIndex]);
        }

        double loopMilliseconds = ReadLoopDuration(characterRoot);
        return new CharacterPreviewData(
            previewFrames,
            TimeSpan.FromMilliseconds(Math.Max(16, loopMilliseconds / previewFrames.Count)),
            animationDirectory);
    }

    private static Dictionary<string, string> ReadPathMap(string path)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
        {
            return values;
        }

        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith(';'))
            {
                continue;
            }

            int separator = line.IndexOf('=');
            if (separator > 0)
            {
                values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }

        return values;
    }

    private static double ReadLoopDuration(string characterRoot)
    {
        string configPath = Path.Combine(characterRoot, "Config.json");
        if (!File.Exists(configPath))
        {
            return 500;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
            if (document.RootElement.TryGetProperty("resultLoopTime", out JsonElement value) &&
                value.TryGetDouble(out double milliseconds) &&
                milliseconds > 0)
            {
                return milliseconds;
            }
        }
        catch (JsonException)
        {
        }

        return 500;
    }

    private static string ResolvePath(string baseDirectory, string path) =>
        Path.GetFullPath(Path.Combine(baseDirectory, path.Replace('/', Path.DirectorySeparatorChar)));
}
