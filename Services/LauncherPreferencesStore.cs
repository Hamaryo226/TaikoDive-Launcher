using System.Text;
using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

public sealed class LauncherPreferencesStore
{
    public LauncherPreferencesStore()
    {
        string? developmentDirectory = Environment.GetEnvironmentVariable(
            "TAIKODIVE_LAUNCHER_SETTINGS_DIRECTORY");
        FilePath = string.IsNullOrWhiteSpace(developmentDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TaikoDiveLauncher",
                "launcher.json")
            : Path.Combine(Path.GetFullPath(developmentDirectory), "launcher.json");
    }

    // Narrow I/O seam: only verification reads, never load/compare or publication.
    internal Func<string, Task<byte[]>> ReadVerificationBytesAsync { get; set; }
        = path => File.ReadAllBytesAsync(path);

    public string FilePath { get; }
    private byte[]? _loadedBytes;
    public bool CanSave { get; private set; }
    public string? ProtectionReason { get; private set; }

    public async Task<LauncherPreferences> LoadAsync()
    {
        try
        {
            using var lease = await AcquireLeaseAsync().ConfigureAwait(false);
            _loadedBytes = await ReadCurrentBytesAsync().ConfigureAwait(false);
            CanSave = true;
            ProtectionReason = null;
            if (_loadedBytes is null) return new LauncherPreferences();
            using var reader = new StreamReader(new MemoryStream(_loadedBytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string json = await reader.ReadToEndAsync().ConfigureAwait(false);
            using var document = System.Text.Json.JsonDocument.Parse(json);
            ValidateObject(document.RootElement, ["closeAfterLaunch", "theme", "homeBannerStyle", "windowPlacement", "backgroundAnimationEnabled"]);
            if (document.RootElement.TryGetProperty("windowPlacement", out var placement) && placement.ValueKind != System.Text.Json.JsonValueKind.Null)
                ValidateObject(placement, ["x", "y", "width", "height", "isMaximized"]);
            var preferences = System.Text.Json.JsonSerializer.Deserialize(json, LauncherJsonContext.Default.LauncherPreferences)!;
            if (preferences.Theme is not ("Dark" or "Light") || preferences.HomeBannerStyle is not ("Ocean" or "Violet" or "Sunset"))
                throw new NotSupportedException();
            return preferences;
        }
        catch (NotSupportedException)
        {
            CanSave = false;
            ProtectionReason = "このランチャーでは扱えない設定が含まれています";
            return new LauncherPreferences();
        }
        catch (System.Text.Json.JsonException)
        {
            CanSave = false;
            ProtectionReason = "ランチャー設定を読み込めませんでした";
            return new LauncherPreferences();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CanSave = false;
            ProtectionReason = "ランチャー設定を読み込めませんでした";
            return new LauncherPreferences();
        }

    }

    private static void ValidateObject(System.Text.Json.JsonElement element, string[] allowed)
    {
        if (element.ValueKind != System.Text.Json.JsonValueKind.Object) throw new NotSupportedException();
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!allowed.Contains(property.Name) || !seen.Add(property.Name)) throw new NotSupportedException();
    }

    private async Task<FileStream> AcquireLeaseAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        for (int attempt = 0; ; attempt++)
        {
            try { return new FileStream(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 100) { await Task.Delay(25).ConfigureAwait(false); }
        }
        // Do not delete the lock file: all cooperating instances must lock the same file.
    }

    public string? LastBackupPath { get; private set; }

    public async Task<string?> BackupAndResetAsync()
    {
        using var lease = await AcquireLeaseAsync().ConfigureAwait(false);
        CanSave = false;
        LastBackupPath = null;
        ProtectionReason = "設定を退避できなかったため、初期化しませんでした";
        byte[]? bytes = await ReadCurrentBytesAsync().ConfigureAwait(false);
        if (!MatchesLoaded(bytes))
        {
            ProtectionReason = "設定ファイルが変更されています";
            throw new InvalidOperationException(ProtectionReason);
        }
        if (bytes is not null)
        {
            string backup = FilePath + ".backup";
            for (int suffix = 1; File.Exists(backup); suffix++)
                backup = FilePath + $".backup.{suffix}";
            using (var stream = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(bytes).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            byte[] verified = await ReadVerificationBytesAsync(backup).ConfigureAwait(false);
            if (!bytes.SequenceEqual(verified))
                throw new IOException("退避ファイルの内容が一致しません。");
            LastBackupPath = backup;
        }
        ProtectionReason = "設定の初期化を完了できませんでした";
        await PublishAsync(new LauncherPreferences()).ConfigureAwait(false);
        CanSave = true;
        ProtectionReason = null;
        return LastBackupPath;
    }

    public async Task SaveAsync(LauncherPreferences preferences)
    {
        using var lease = await AcquireLeaseAsync().ConfigureAwait(false);
        if (!CanSave) throw new InvalidOperationException(ProtectionReason ?? "設定を読み直してください");
        byte[]? current;
        try
        {
            current = await ReadCurrentBytesAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CanSave = false;
            ProtectionReason = "ランチャー設定を読み込めませんでした";
            throw;
        }
        if (!MatchesLoaded(current))
        {
            CanSave = false;
            ProtectionReason = "設定ファイルが変更されています";
            throw new InvalidOperationException(ProtectionReason);
        }
        await PublishAsync(preferences).ConfigureAwait(false);
    }

    private bool MatchesLoaded(byte[]? current) => current is null
        ? _loadedBytes is null
        : _loadedBytes is not null && current.SequenceEqual(_loadedBytes);

    private async Task<byte[]?> ReadCurrentBytesAsync()
    {
        try { return await File.ReadAllBytesAsync(FilePath).ConfigureAwait(false); }
        catch (FileNotFoundException) { return null; }
    }

    private async Task PublishAsync(LauncherPreferences preferences)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(
            preferences,
            LauncherJsonContext.Default.LauncherPreferences);
        string temporary = FilePath + ".launcher.tmp";
        await File.WriteAllTextAsync(temporary, json + Environment.NewLine, new UTF8Encoding(false)).ConfigureAwait(false);
        try
        {
            File.Move(temporary, FilePath, overwrite: _loadedBytes is not null);
            byte[] expected = Encoding.UTF8.GetBytes(json + Environment.NewLine);
            byte[] actual = await ReadVerificationBytesAsync(FilePath).ConfigureAwait(false);
            if (!expected.SequenceEqual(actual)) throw new IOException("保存後の内容が一致しません。");
            _loadedBytes = expected;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CanSave = false;
            ProtectionReason = "保存結果を確認できませんでした。設定を読み直してください";
            throw;
        }
        finally
        {
            try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
