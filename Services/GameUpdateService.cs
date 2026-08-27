using System.Net.Http.Headers;
using System.Text.Json;
using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

public sealed class GameUpdateService
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
    private readonly UpdateChannel _channel;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private static readonly SemaphoreSlim ApplyLock = new(1, 1);
    private readonly object _stateLock = new();
    private GameUpdateState _state;
    private string _statusMessage;
    private GameUpdateManifest? _availableUpdate;
    private double? _progressPercentage;

    public GameUpdateService(Func<TaikoDiveInstallation?> installationProvider)
        : this(
            CreateHttpClient(),
            DefaultManifestUri,
            installationProvider,
            GameProcessService.IsRunning,
            GamePackageKeyProvider.GetPackageKey,
            new GameUpdatePackageExtractor(),
            UpdateChannel.Game)
    {
    }

    public static GameUpdateService CreateAssets(Func<TaikoDiveInstallation?> installationProvider) => new(
        CreateHttpClient(),
        UpdateChannel.Assets.ManifestUri,
        installationProvider,
        GameProcessService.IsRunning,
        GamePackageKeyProvider.GetPackageKey,
        new GameUpdatePackageExtractor(AssetUpdatePathPolicy.NormalizeAndValidate),
        UpdateChannel.Assets);

    internal GameUpdateService(
        HttpClient httpClient,
        Uri manifestUri,
        Func<TaikoDiveInstallation?> installationProvider,
        Func<bool> isGameRunning,
        Func<string?> packageKeyProvider,
        GameUpdatePackageExtractor extractor,
        UpdateChannel? channel = null)
    {
        _httpClient = httpClient;
        _manifestUri = manifestUri;
        _installationProvider = installationProvider;
        _isGameRunning = isGameRunning;
        _packageKeyProvider = packageKeyProvider;
        _extractor = extractor;
        _channel = channel ?? UpdateChannel.Game with { ManifestUri = manifestUri };
        _statusMessage = $"公開チャンネルから{_channel.DisplayName}の更新を確認できます。";
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

    public double? ProgressPercentage
    {
        get { lock (_stateLock) { return _progressPercentage; } }
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

            SetState(GameUpdateState.Checking, $"{_channel.DisplayName}のアップデートを確認しています…", null);
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
            ValidateManifest(manifest, _channel);

            string currentVersion = DetectCurrentVersion(installation);
            if (GameUpdatePackageNaming.CompareVersions(manifest!.Version, currentVersion) <= 0)
            {
                SetState(GameUpdateState.UpToDate, $"最新版です（v{currentVersion}）。", null);
                return;
            }

            SetState(GameUpdateState.Available, $"{_channel.DisplayName} v{manifest.Version}を利用できます。", manifest);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetState(GameUpdateState.Idle, "アップデート確認を中止しました。", null);
        }
        catch (Exception ex)
        {
            SetState(GameUpdateState.Failed, $"{_channel.DisplayName}の更新を確認できませんでした: {ex.Message}", null);
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
        bool applyLockTaken = false;
        try
        {
            GameUpdateManifest? update = AvailableUpdate;
            TaikoDiveInstallation? installation = _installationProvider();
            if (update is null)
            {
                return OperationResult.Failure($"利用できる{_channel.DisplayName}アップデートがありません。");
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
                return OperationResult.Failure("このランチャーには更新用キーが設定されていません。");
            }

            await ApplyLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            applyLockTaken = true;
            operationDirectory = Path.Combine(GetUpdatesRoot(), "staging", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(operationDirectory);
            string packagePath = Path.Combine(operationDirectory, update.PackageFileName);
            SetState(GameUpdateState.Downloading, $"{_channel.DisplayName} v{update.Version}をダウンロードしています…", update, 0);
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

            SetState(GameUpdateState.Applying, "検証済みファイルを適用しています…", update, 0);
            string backupDirectory = CreateBackupDirectory(update);
            await ApplyWithRollbackAsync(
                Path.Combine(extractionDirectory, "files"),
                installation.BuildDirectory,
                backupDirectory,
                package.Files,
                cancellationToken,
                _channel.NormalizePath,
                ReportProgress).ConfigureAwait(false);
            await SaveInstalledUpdateAsync(installation, update, cancellationToken).ConfigureAwait(false);

            SetState(GameUpdateState.Completed, $"{_channel.DisplayName} v{update.Version}へ更新しました。", null, 100);
            return OperationResult.Success($"{_channel.DisplayName} v{update.Version}へ更新しました。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetState(GameUpdateState.Available, $"{_channel.DisplayName}のアップデートを中止しました。", AvailableUpdate);
            return OperationResult.Failure("アップデートを中止しました。");
        }
        catch (Exception ex)
        {
            SetState(GameUpdateState.Failed, $"{_channel.DisplayName}のアップデートに失敗しました: {ex.Message}", AvailableUpdate);
            return OperationResult.Failure(ex.Message);
        }
        finally
        {
            if (operationDirectory is not null)
            {
                TryDeleteDirectory(operationDirectory);
            }
            if (applyLockTaken)
            {
                ApplyLock.Release();
            }
            _operationLock.Release();
        }
    }

    internal static void ValidateManifest(GameUpdateManifest? manifest)
        => ValidateManifest(manifest, UpdateChannel.Game);

    internal static void ValidateAssetManifest(GameUpdateManifest? manifest)
        => ValidateManifest(manifest, UpdateChannel.Assets);

    private static void ValidateManifest(GameUpdateManifest? manifest, UpdateChannel channel)
    {
        if (manifest is null
            || !GameUpdatePackageNaming.IsVersion(manifest.Version)
            || !GameUpdatePackageNaming.IsRevision(manifest.Revision)
            || !channel.PackageMatches(manifest.PackageFileName, manifest.Version, manifest.Revision)
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
            throw new InvalidDataException($"{channel.DisplayName}更新マニフェストが不正です。");
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
        CancellationToken cancellationToken,
        Func<string, string>? normalizePath = null,
        Action<long, long>? progress = null)
    {
        normalizePath ??= GameUpdatePathPolicy.NormalizeAndValidate;
        Directory.CreateDirectory(backupDirectory);
        List<string> replaced = [];
        List<string> created = [];
        long completedFiles = 0;
        try
        {
            foreach (GamePackageFile file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relativePath = normalizePath(file.Path);
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

                completedFiles++;
                progress?.Invoke(completedFiles, files.Count);
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
            byte[] buffer = new byte[81920];
            long downloadedBytes = 0;
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                downloadedBytes += bytesRead;
                ReportProgress(downloadedBytes, manifest.Size);
            }
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

    private string DetectCurrentVersion(TaikoDiveInstallation? installation)
    {
        InstalledGameUpdate? installed = LoadInstalledUpdate();
        if (installed is not null
            && installation is not null
            && IsStateForInstallation(installed, installation)
            && GameUpdatePackageNaming.IsVersion(installed.Version))
        {
            return installed.Version;
        }

        // The update channel has its own monotonically increasing version. The executable's
        // product version belongs to TaikoDive's build metadata and is not comparable with it.
        // Until this launcher applies the first managed update, use the channel baseline.
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

    private InstalledGameUpdate? LoadInstalledUpdate()
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

    private async Task SaveInstalledUpdateAsync(
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

    private void SetState(
        GameUpdateState state,
        string message,
        GameUpdateManifest? availableUpdate,
        double? progressPercentage = null)
    {
        lock (_stateLock)
        {
            _state = state;
            _statusMessage = message;
            _availableUpdate = availableUpdate;
            _progressPercentage = progressPercentage;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReportProgress(long completed, long total)
    {
        if (total <= 0)
        {
            return;
        }

        double percentage = Math.Floor(Math.Clamp(completed * 100d / total, 0, 100));
        bool changed;
        lock (_stateLock)
        {
            changed = _progressPercentage != percentage;
            _progressPercentage = percentage;
        }

        if (changed)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
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

    private string GetUpdatesRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TaikoDiveLauncher",
        _channel.StorageDirectoryName);

    private string GetInstalledStatePath() => Path.Combine(GetUpdatesRoot(), "installed.json");

    private string CreateBackupDirectory(GameUpdateManifest update) => Path.Combine(
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

    internal sealed record UpdateChannel(
        string DisplayName,
        Uri ManifestUri,
        string StorageDirectoryName,
        Func<string, string, string, bool> PackageMatches,
        Func<string, string> NormalizePath)
    {
        public static UpdateChannel Game { get; } = new(
            "TaikoDive",
            DefaultManifestUri,
            "game-updates",
            GameUpdatePackageNaming.Matches,
            GameUpdatePathPolicy.NormalizeAndValidate);

        public static UpdateChannel Assets { get; } = new(
            "TaikoDive Asset",
            new Uri("https://github.com/Hamaryo226/TaikoDive-Launcher/releases/download/assets-stable/assets-update-manifest.json"),
            "asset-updates",
            AssetUpdatePackageNaming.Matches,
            AssetUpdatePathPolicy.NormalizeAndValidate);
    }

}
