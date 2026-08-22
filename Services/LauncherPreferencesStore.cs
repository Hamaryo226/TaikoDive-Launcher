using System.Text;
using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

public sealed class LauncherPreferencesStore
{
    public string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TaikoDiveLauncher",
        "launcher.json");

    public async Task<LauncherPreferences> LoadAsync()
    {
        if (!File.Exists(FilePath))
        {
            return new LauncherPreferences();
        }

        try
        {
            await using FileStream stream = File.OpenRead(FilePath);
            return await System.Text.Json.JsonSerializer.DeserializeAsync(
                    stream,
                    LauncherJsonContext.Default.LauncherPreferences)
                .ConfigureAwait(false) ?? new LauncherPreferences();
        }
        catch (System.Text.Json.JsonException)
        {
            return new LauncherPreferences();
        }
        catch (IOException)
        {
            return new LauncherPreferences();
        }

    }

    public Task SaveAsync(LauncherPreferences preferences)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(
            preferences,
            LauncherJsonContext.Default.LauncherPreferences);
        return FilePersistence.WriteTextAtomicAsync(
            FilePath,
            json + Environment.NewLine,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            createBackup: false);
    }
}
