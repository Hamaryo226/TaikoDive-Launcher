using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

public sealed partial class GameUpdateService
{
    private const long MaximumDownloadSize = 4L * 1024 * 1024 * 1024;
    private static readonly Uri DefaultManifestUri = new(
        "https://github.com/Hamaryo226/TaikoDive-Launcher/releases/download/game-stable/game-update-manifest.json");

    private readonly HttpClient _httpClient;
    private readonly Uri _manifestUri;
    private readonly Func<TaikoDiveInstallation?> _installationProvider;
    private readonly Func<bool> _isGameRunning;
    private readonly Func<string?> _packageKeyProvider;
    private readonly GameUpdatePackageExtractor _extractor;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly object _stateLock = new();
    private GameUpdateState _state;
    private string _statusMessage = "公開チャンネルからTaikoDiveの更新を確認できます。";
    private GameUpdateManifest? _availableUpdate;

    public GameUpdateService(Func<TaikoDiveInstallation?> installationProvider)
        : this(
            CreateHttpClient(),
            DefaultManifestUri,
            installationProvider,
            GameProcessService.IsRunning,
            GamePackageKeyProvider.GetPackageKey,
            new GameUpdatePackageExtractor())
    {
    }

    internal GameUpdateService(
        HttpClient httpClient,
        Uri manifestUri,
        Func<TaikoDiveInstallation?> installationProvider,
        Func<bool> isGameRunning,
        Func<string?> packageKeyProvider,
        GameUpdatePackageExtractor extractor)
    {
        _httpClient = httpClient;
        _manifestUri = manifestUri;
        _installationProvider = installationProvider;
        _isGameRunning = isGameRunning;
        _packageKeyProvider = packageKeyProvider;
        _extractor = extractor;
    }

    public event EventHandler? StateChanged;

    public GameUpdateState State
    {
        get { lock (_stateLock) { return _state; } }
    }

    public string StatusMessage
    {
        get { lock (_stateLock) { return _statusMessage; } }
    }

    public GameUpdateManifest? AvailableUpdate
    {
        get { lock (_stateLock) { return _availableUpdate; } }
    }

    public string CurrentVersionText => DetectCurrentVersion(_installationProvider());

    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!await _operationLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            TaikoDiveInstallation? installation = _installationProvider();
            if (installation is null || !installation.IsValid)
            {
                SetState(GameUpdateState.Failed, "TaikoDive.exeが見つからないため更新を確認できません。", null);
                return;
            }

            SetState(GameUpdateState.Checking, "TaikoDiveのアップデートを確認しています…", null);
            using HttpRequestMessage request = new(HttpMethod.Get, AddCacheBuster(_manifestUri));
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            GameUpdateManifest? manifest = await JsonSerializer.DeserializeAsync(
                    stream,
                    LauncherJsonContext.Default.GameUpdateManifest,
                    cancellationToken)
                .ConfigureAwait(false);
            ValidateManifest(manifest);

            string currentVersion = DetectCurrentVersion(installation);
            if (GameUpdatePackageNaming.CompareVersions(manifest!.Version, currentVersion) <= 0)
            {
                SetState(GameUpdateState.UpToDate, $"最新版です（v{currentVersion}）。", null);
                return;
            }

            SetState(GameUpdateState.Available, $"TaikoDive v{manifest.Version}を利用できます。", manifest);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetState(GameUpdateState.Idle, "アップデート確認を中止しました。", null);
        }
        catch (Exception ex)
        {
            SetState(GameUpdateState.Failed, $"TaikoDiveの更新を確認できませんでした: {ex.Message}", null);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<OperationResult> DownloadAndApplyAsync(CancellationToken cancellationToken = default)
    {
        if (!await _operationLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult.Failure("別のアップデート処理が進行中です。");
        }

        string? operationDirectory = null;
        try
        {
            GameUpdateManifest? update = AvailableUpdate;
            TaikoDiveInstallation? installation = _installationProvider();
            if (update is null)
            {
                return OperationResult.Failure("利用できるTaikoDiveアップデートがありません。");
            }
            if (installation is null || !installation.IsValid)
            {
                return OperationResult.Failure("TaikoDive.exeが見つかりません。");
            }
            if (_isGameRunning())
            {
                return OperationResult.Failure("TaikoDiveを終了してからアップデートしてください。");
            }

            string? packageKey = _packageKeyProvider();
            if (string.IsNullOrWhiteSpace(packageKey))
            {
                return OperationResult.Failure("このランチャーにはゲーム更新用キーが設定されていません。");
            }

            operationDirectory = Path.Combine(GetUpdatesRoot(), "staging", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(operationDirectory);
            string packagePath = Path.Combine(operationDirectory, update.PackageFileName);
            SetState(GameUpdateState.Downloading, $"TaikoDive v{update.Version}をダウンロードしています…", update);
            await DownloadAndVerifyAsync(packagePath, update, cancellationToken).ConfigureAwait(false);

            SetState(GameUpdateState.Verifying, "暗号化パッケージを検証しています…", update);
            string extractionDirectory = Path.Combine(operationDirectory, "extracted");
            GamePackageManifest package = await _extractor.ExtractAndVerifyAsync(
                packagePath,
                extractionDirectory,
                update,
                packageKey,
                cancellationToken).ConfigureAwait(false);

            if (_isGameRunning())
            {
                throw new InvalidOperationException("TaikoDiveが起動しました。終了してからもう一度お試しください。");
            }

            SetState(GameUpdateState.Applying, "検証済みファイルを適用しています…", update);
            string backupDirectory = CreateBackupDirectory(update);
            await ApplyWithRollbackAsync(
                Path.Combine(extractionDirectory, "files"),
                installation.BuildDirectory,
                backupDirectory,
                package.Files,
                cancellationToken).ConfigureAwait(false);
            await SaveInstalledUpdateAsync(installation, update, cancellationToken).ConfigureAwait(false);

            SetState(GameUpdateState.Completed, $"TaikoDive v{update.Version}へ更新しました。", null);
            return OperationResult.Success($"TaikoDive v{update.Version}へ更新しました。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetState(GameUpdateState.Available, "TaikoDiveのアップデートを中止しました。", AvailableUpdate);
            return OperationResult.Failure("アップデートを中止しました。");
        }
        catch (Exception ex)
        {
            SetState(GameUpdateState.Failed, $"TaikoDiveのアップデートに失敗しました: {ex.Message}", AvailableUpdate);
            return OperationResult.Failure(ex.Message);
        }
        finally
        {
            if (operationDirectory is not null)
            {
                TryDeleteDirectory(operationDirectory);
            }
            _operationLock.Release();
        }
    }

    internal static void ValidateManifest(GameUpdateManifest? manifest)
    {
        if (manifest is null
            || !GameUpdatePackageNaming.IsVersion(manifest.Version)
            || !GameUpdatePackageNaming.IsRevision(manifest.Revision)
            || !GameUpdatePackageNaming.Matches(manifest.PackageFileName, manifest.Version, manifest.Revision)
            || !GameUpdatePackageExtractor.IsHex(manifest.Sha256, 64)
            || manifest.Size is <= 0 or > MaximumDownloadSize
            || manifest.PublishedAt == default
            || !Uri.TryCreate(manifest.PackageUrl, UriKind.Absolute, out Uri? packageUri)
            || packageUri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(packageUri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFileName(packageUri.AbsolutePath), manifest.PackageFileName, StringComparison.Ordinal)
            || !string.Equals(manifest.Archive.Format, "zip", StringComparison.Ordinal)
            || !string.Equals(manifest.Archive.Encryption, "winzip-aes-256", StringComparison.Ordinal)
            || !string.Equals(manifest.Archive.Payload, "payload.bin", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(manifest.Archive.KeyId))
        {
            throw new InvalidDataException("TaikoDive更新マニフェストが不正です。");
        }

        if (!string.Equals(manifest.Archive.KeyId, GamePackageKeyProvider.KeyId, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"更新キー{manifest.Archive.KeyId}には未対応です。ランチャーを先に更新してください。");
        }

        manifest.Revision = manifest.Revision.ToLowerInvariant();
        manifest.Sha256 = manifest.Sha256.ToUpperInvariant();
    }

    internal static async Task ApplyWithRollbackAsync(
        string stagedFilesDirectory,
        string targetDirectory,
        string backupDirectory,
        IReadOnlyList<GamePackageFile> files,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(backupDirectory);
        List<string> replaced = [];
        List<string> created = [];
        try
        {
            foreach (GamePackageFile file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relativePath = GameUpdatePathPolicy.NormalizeAndValidate(file.Path);
                string source = GameUpdatePackageExtractor.ResolveContainedPath(stagedFilesDirectory, relativePath);
                string target = GameUpdatePackageExtractor.ResolveContainedPath(targetDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                if (File.Exists(target))
                {
                    string backup = GameUpdatePackageExtractor.ResolveContainedPath(backupDirectory, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(target, backup, overwrite: false);
                    replaced.Add(relativePath);
                }
                else
                {
                    created.Add(relativePath);
                }

                string pending = target + ".update.pending";
                try
                {
                    File.Copy(source, pending, overwrite: true);
                    File.Move(pending, target, overwrite: true);
                }
                finally
                {
                    TryDeleteFile(pending);
                }
            }
        }
        catch
        {
            foreach (string relativePath in replaced.AsEnumerable().Reverse())
            {
                string backup = GameUpdatePackageExtractor.ResolveContainedPath(backupDirectory, relativePath);
                string target = GameUpdatePackageExtractor.ResolveContainedPath(targetDirectory, relativePath);
                if (File.Exists(backup))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(backup, target, overwrite: true);
                }
            }
            foreach (string relativePath in created.AsEnumerable().Reverse())
            {
                TryDeleteFile(GameUpdatePackageExtractor.ResolveContainedPath(targetDirectory, relativePath));
            }
            throw;
        }
    }

    private async Task DownloadAndVerifyAsync(
        string destinationPath,
        GameUpdateManifest manifest,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(manifest.PackageUrl));
        using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long contentLength
            && (contentLength != manifest.Size || contentLength > MaximumDownloadSize))
        {
            throw new InvalidDataException("更新パッケージのサイズがマニフェストと一致しません。");
        }

        await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (FileStream destination = new(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }

        FileInfo file = new(destinationPath);
        if (file.Length != manifest.Size || file.Length > MaximumDownloadSize)
        {
            throw new InvalidDataException("更新パッケージのサイズがマニフェストと一致しません。");
        }
        string hash = await GameUpdatePackageExtractor.ComputeSha256Async(destinationPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(hash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("更新パッケージのSHA-256が一致しません。");
        }
    }

    private static string DetectCurrentVersion(TaikoDiveInstallation? installation)
    {
        InstalledGameUpdate? installed = LoadInstalledUpdate();
        if (installed is not null
            && installation is not null
            && IsStateForInstallation(installed, installation)
            && GameUpdatePackageNaming.IsVersion(installed.Version))
        {
            return installed.Version;
        }

        if (installation?.IsValid == true)
        {
            try
            {
                string? productVersion = FileVersionInfo.GetVersionInfo(installation.ExecutablePath).ProductVersion;
                Match match = VersionInTextRegex().Match(productVersion ?? string.Empty);
                if (match.Success && GameUpdatePackageNaming.IsVersion(match.Value))
                {
                    return match.Value;
                }
            }
            catch
            {
                // An unversioned development executable is treated as 0.0.0.
            }
        }
        return "0.0.0";
    }

    private static bool IsStateForInstallation(InstalledGameUpdate state, TaikoDiveInstallation installation)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(state.BuildDirectory)
                && string.Equals(
                    Path.GetFullPath(state.BuildDirectory),
                    installation.BuildDirectory,
                    StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static InstalledGameUpdate? LoadInstalledUpdate()
    {
        try
        {
            string path = GetInstalledStatePath();
            if (!File.Exists(path))
            {
                return null;
            }
            using FileStream stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, LauncherJsonContext.Default.InstalledGameUpdate);
        }
        catch
        {
            return null;
        }
    }

    private static async Task SaveInstalledUpdateAsync(
        TaikoDiveInstallation installation,
        GameUpdateManifest update,
        CancellationToken cancellationToken)
    {
        string path = GetInstalledStatePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporaryPath = path + ".tmp";
        InstalledGameUpdate state = new()
        {
            BuildDirectory = installation.BuildDirectory,
            Version = update.Version,
            Revision = update.Revision,
            InstalledAt = DateTimeOffset.UtcNow,
        };
        await using (FileStream stream = new(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                state,
                LauncherJsonContext.Default.InstalledGameUpdate,
                cancellationToken).ConfigureAwait(false);
        }
        File.Move(temporaryPath, path, overwrite: true);
    }

    private void SetState(GameUpdateState state, string message, GameUpdateManifest? availableUpdate)
    {
        lock (_stateLock)
        {
            _state = state;
            _statusMessage = message;
            _availableUpdate = availableUpdate;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new() { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TaikoDive.Launcher/1.0");
        return client;
    }

    private static Uri AddCacheBuster(Uri uri)
    {
        UriBuilder builder = new(uri) { Query = $"v={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}" };
        return builder.Uri;
    }

    private static string GetUpdatesRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TaikoDiveLauncher",
        "game-updates");

    private static string GetInstalledStatePath() => Path.Combine(GetUpdatesRoot(), "installed.json");

    private static string CreateBackupDirectory(GameUpdateManifest update) => Path.Combine(
        GetUpdatesRoot(),
        "backups",
        $"v{update.Version}-{update.Revision[..7]}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}");

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) { Directory.Delete(path, recursive: true); } } catch { }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) { File.Delete(path); } } catch { }
    }

    [GeneratedRegex(@"\d+\.\d+\.\d+", RegexOptions.CultureInvariant)]
    private static partial Regex VersionInTextRegex();
}
