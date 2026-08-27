using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

public sealed class LauncherUpdateService
{
    private const string UpdateTag = "launcher-main";
    private const string ExecutableName = "TaikoDive.Launcher.exe";
    private const long MaximumDownloadSize = 512L * 1024 * 1024;
    private const int MaximumReleaseNotesLength = 4000;
    private static readonly Uri DefaultManifestUri = new(
        $"https://github.com/Hamaryo226/TaikoDive-Launcher/releases/download/{UpdateTag}/update-manifest.json");
    private static readonly Uri DefaultExecutableUri = new(
        $"https://github.com/Hamaryo226/TaikoDive-Launcher/releases/download/{UpdateTag}/{ExecutableName}");

    private readonly HttpClient _httpClient;
    private readonly string _currentRevision;
    private readonly Uri _manifestUri;
    private readonly Uri _executableUri;
    private readonly Func<string?> _processPathProvider;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly object _stateLock = new();
    private LauncherUpdateState _state;
    private string _statusMessage = "起動時にmainブランチの最新版を確認します。";
    private LauncherUpdateManifest? _availableUpdate;
    private LauncherUpdateManifest? _latestUpdate;
    private double? _progressPercentage;

    public LauncherUpdateService()
        : this(
            CreateHttpClient(),
            LauncherBuildInfo.CurrentRevision,
            DefaultManifestUri,
            DefaultExecutableUri,
            () => Environment.ProcessPath)
    {
    }

    internal LauncherUpdateService(
        HttpClient httpClient,
        string currentRevision,
        Uri manifestUri,
        Uri executableUri,
        Func<string?> processPathProvider)
    {
        _httpClient = httpClient;
        _currentRevision = currentRevision;
        _manifestUri = manifestUri;
        _executableUri = executableUri;
        _processPathProvider = processPathProvider;
    }

    public event EventHandler? StateChanged;

    public LauncherUpdateState State
    {
        get
        {
            lock (_stateLock)
            {
                return _state;
            }
        }
    }

    public string StatusMessage
    {
        get
        {
            lock (_stateLock)
            {
                return _statusMessage;
            }
        }
    }

    public LauncherUpdateManifest? AvailableUpdate
    {
        get
        {
            lock (_stateLock)
            {
                return _availableUpdate;
            }
        }
    }

    public LauncherUpdateManifest? LatestUpdate
    {
        get
        {
            lock (_stateLock)
            {
                return _latestUpdate;
            }
        }
    }

    public double? ProgressPercentage
    {
        get
        {
            lock (_stateLock)
            {
                return _progressPercentage;
            }
        }
    }

    public string CurrentVersionText => LauncherBuildInfo.DisplayVersion;

    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!await _operationLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            SetState(LauncherUpdateState.Checking, "アップデートを確認しています…", null);
            using HttpRequestMessage request = new(HttpMethod.Get, AddCacheBuster(_manifestUri));
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            LauncherUpdateManifest? manifest = await JsonSerializer.DeserializeAsync(
                    stream,
                    LauncherJsonContext.Default.LauncherUpdateManifest,
                    cancellationToken)
                .ConfigureAwait(false);
            ValidateManifest(manifest);
            SetLatestUpdate(manifest!);

            if (string.Equals(manifest!.Revision, _currentRevision, StringComparison.OrdinalIgnoreCase))
            {
                SetState(LauncherUpdateState.UpToDate, "最新版です。", null);
                return;
            }

            SetState(
                LauncherUpdateState.Available,
                $"mainの更新があります（{ShortRevision(manifest.Revision)}）。",
                manifest);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetState(LauncherUpdateState.Idle, "アップデート確認を中止しました。", null);
        }
        catch (Exception ex)
        {
            SetState(LauncherUpdateState.Failed, $"アップデートを確認できませんでした: {ex.Message}", null);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<OperationResult> DownloadAndStartInstallerAsync(CancellationToken cancellationToken = default)
    {
        if (!await _operationLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult.Failure("別のアップデート処理が進行中です。");
        }

        try
        {
            LauncherUpdateManifest? manifest = AvailableUpdate;
            if (manifest is null)
            {
                return OperationResult.Failure("利用できるアップデートがありません。");
            }

            string? targetPath = _processPathProvider();
            if (string.IsNullOrWhiteSpace(targetPath)
                || !string.Equals(Path.GetFileName(targetPath), ExecutableName, StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult.Failure("実行中のTaikoDive.Launcher.exeを特定できません。");
            }

            EnsureTargetDirectoryIsWritable(targetPath);
            string stagedPath = GetStagedExecutablePath(manifest.Revision);
            Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
            SetState(LauncherUpdateState.Downloading, "アップデートをダウンロードしています…", manifest, 0);
            await DownloadAndVerifyAsync(stagedPath, manifest, cancellationToken).ConfigureAwait(false);

            ProcessStartInfo installer = new()
            {
                FileName = stagedPath,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(stagedPath)!,
            };
            installer.ArgumentList.Add("--apply-update");
            installer.ArgumentList.Add("--target");
            installer.ArgumentList.Add(Path.GetFullPath(targetPath));
            installer.ArgumentList.Add("--parent");
            installer.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            installer.ArgumentList.Add("--working-directory");
            installer.ArgumentList.Add(Path.GetDirectoryName(Path.GetFullPath(targetPath))!);
            installer.ArgumentList.Add("--sha256");
            installer.ArgumentList.Add(manifest.Sha256);

            if (Process.Start(installer) is null)
            {
                throw new InvalidOperationException("アップデーターを起動できませんでした。");
            }

            SetState(LauncherUpdateState.StartingInstaller, "更新を適用するため再起動します…", manifest, 100);
            return OperationResult.Success("アップデーターを起動しました。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetState(LauncherUpdateState.Available, "アップデートを中止しました。", AvailableUpdate);
            return OperationResult.Failure("アップデートを中止しました。");
        }
        catch (Exception ex)
        {
            SetState(LauncherUpdateState.Failed, $"アップデートに失敗しました: {ex.Message}", AvailableUpdate);
            return OperationResult.Failure(ex.Message);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    internal static bool TryParseApplyCommand(string[] args, out PendingUpdateCommand? command)
    {
        command = null;
        if (!args.Contains("--apply-update", StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        string? target = GetArgumentValue(args, "--target");
        string? parentText = GetArgumentValue(args, "--parent");
        string? workingDirectory = GetArgumentValue(args, "--working-directory");
        string? sha256 = GetArgumentValue(args, "--sha256");
        if (string.IsNullOrWhiteSpace(target)
            || !string.Equals(Path.GetFileName(target), ExecutableName, StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(parentText, out int parentProcessId)
            || parentProcessId <= 0
            || string.IsNullOrWhiteSpace(workingDirectory)
            || !IsHex(sha256, 64))
        {
            return false;
        }

        command = new PendingUpdateCommand(
            Path.GetFullPath(target),
            parentProcessId,
            Path.GetFullPath(workingDirectory),
            sha256!.ToUpperInvariant());
        return true;
    }

    internal static async Task<bool> ApplyPendingUpdateAsync(
        PendingUpdateCommand command,
        CancellationToken cancellationToken = default)
    {
        string? sourcePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return false;
        }

        try
        {
            string sourceHash = await ComputeSha256Async(sourcePath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(sourceHash, command.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("更新ファイルのSHA-256が一致しません。");
            }

            await WaitForProcessExitAsync(command.ParentProcessId, cancellationToken).ConfigureAwait(false);
            string replacementPath = command.TargetPath + ".update.pending";
            File.Copy(sourcePath, replacementPath, overwrite: true);
            File.Move(replacementPath, command.TargetPath, overwrite: true);

            Process.Start(new ProcessStartInfo
            {
                FileName = command.TargetPath,
                UseShellExecute = true,
                WorkingDirectory = command.WorkingDirectory,
            });
            return true;
        }
        catch (Exception ex)
        {
            WriteUpdateError(ex);
            TryRestartExistingLauncher(command);
            return false;
        }
    }

    internal static async Task CleanupStagedUpdatesAsync()
    {
        string root = GetUpdateRootDirectory();
        try
        {
            if (Directory.Exists(root))
            {
                await Task.Delay(1000).ConfigureAwait(false);
                Directory.Delete(root, recursive: true);
            }
        }
        catch
        {
            // A running updater can keep its own file locked. The next launch retries cleanup.
        }
    }

    private async Task DownloadAndVerifyAsync(
        string stagedPath,
        LauncherUpdateManifest manifest,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, AddCacheBuster(_executableUri));
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? contentLength = response.Content.Headers.ContentLength;
        if (contentLength is > MaximumDownloadSize || (contentLength.HasValue && contentLength.Value != manifest.Size))
        {
            throw new InvalidDataException("更新ファイルのサイズがマニフェストと一致しません。");
        }

        string temporaryPath = stagedPath + ".download";
        try
        {
            await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (FileStream destination = new(
                temporaryPath,
                FileMode.Create,
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

            FileInfo downloadedFile = new(temporaryPath);
            if (downloadedFile.Length != manifest.Size || downloadedFile.Length > MaximumDownloadSize)
            {
                throw new InvalidDataException("更新ファイルのサイズがマニフェストと一致しません。");
            }

            string actualHash = await ComputeSha256Async(temporaryPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("更新ファイルのSHA-256が一致しません。");
            }

            File.Move(temporaryPath, stagedPath, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static void ValidateManifest(LauncherUpdateManifest? manifest)
    {
        if (manifest is null
            || !IsHex(manifest.Revision, 40)
            || !IsHex(manifest.Sha256, 64)
            || manifest.ReleaseNotes is null
            || manifest.ReleaseNotes.Length > MaximumReleaseNotesLength
            || manifest.Size is <= 0 or > MaximumDownloadSize)
        {
            throw new InvalidDataException("更新マニフェストが不正です。");
        }

        manifest.Revision = manifest.Revision.ToLowerInvariant();
        manifest.ReleaseNotes = manifest.ReleaseNotes.Trim();
        manifest.Sha256 = manifest.Sha256.ToUpperInvariant();
    }

    private void SetLatestUpdate(LauncherUpdateManifest latestUpdate)
    {
        lock (_stateLock)
        {
            _latestUpdate = latestUpdate;
        }
    }

    private void SetState(
        LauncherUpdateState state,
        string message,
        LauncherUpdateManifest? availableUpdate,
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

    private void ReportProgress(long completedBytes, long totalBytes)
    {
        if (totalBytes <= 0)
        {
            return;
        }

        double percentage = Math.Floor(Math.Clamp(completedBytes * 100d / totalBytes, 0, 100));
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
        HttpClient client = new()
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TaikoDive.Launcher/1.0");
        return client;
    }

    private static Uri AddCacheBuster(Uri uri)
    {
        UriBuilder builder = new(uri)
        {
            Query = $"v={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
        };
        return builder.Uri;
    }

    private static string GetStagedExecutablePath(string revision)
    {
        return Path.Combine(GetUpdateRootDirectory(), revision, ExecutableName);
    }

    private static string GetUpdateRootDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaikoDiveLauncher",
            "updates");
    }

    private static void EnsureTargetDirectoryIsWritable(string targetPath)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(targetPath))
            ?? throw new InvalidOperationException("ランチャーの配置先を特定できません。");
        string probePath = Path.Combine(directory, $".launcher-update-{Guid.NewGuid():N}.tmp");
        try
        {
            using FileStream probe = new(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        catch (UnauthorizedAccessException)
        {
            throw new UnauthorizedAccessException("ランチャーの配置先へ書き込めません。書き込み可能なTaikoDiveフォルダーへ移動してください。");
        }
        finally
        {
            TryDelete(probePath);
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static async Task WaitForProcessExitAsync(int processId, CancellationToken cancellationToken)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            // The previous launcher already exited.
        }
    }

    private static string? GetArgumentValue(string[] args, string name)
    {
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static bool IsHex(string? value, int expectedLength)
    {
        return value?.Length == expectedLength && value.All(Uri.IsHexDigit);
    }

    private static string ShortRevision(string revision)
    {
        return revision.Length <= 7 ? revision : revision[..7];
    }

    private static void TryRestartExistingLauncher(PendingUpdateCommand command)
    {
        try
        {
            if (File.Exists(command.TargetPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = command.TargetPath,
                    UseShellExecute = true,
                    WorkingDirectory = command.WorkingDirectory,
                });
            }
        }
        catch
        {
        }
    }

    private static void WriteUpdateError(Exception exception)
    {
        try
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TaikoDiveLauncher",
                "update-error.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}");
        }
        catch
        {
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
        }
    }
}

internal sealed record PendingUpdateCommand(
    string TargetPath,
    int ParentProcessId,
    string WorkingDirectory,
    string Sha256);

internal static class LauncherBuildInfo
{
    private static readonly string InformationalVersion =
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "1.0.0";

    public static string CurrentRevision { get; } = GetRevision(InformationalVersion);

    public static string DisplayVersion => string.IsNullOrWhiteSpace(CurrentRevision)
        ? InformationalVersion
        : $"{InformationalVersion.Split('+')[0]} ({CurrentRevision[..Math.Min(7, CurrentRevision.Length)]})";

    private static string GetRevision(string informationalVersion)
    {
        int separatorIndex = informationalVersion.LastIndexOf('+');
        if (separatorIndex < 0 || separatorIndex == informationalVersion.Length - 1)
        {
            return string.Empty;
        }

        string revision = informationalVersion[(separatorIndex + 1)..];
        return revision.Length == 40 && revision.All(Uri.IsHexDigit)
            ? revision.ToLowerInvariant()
            : string.Empty;
    }
}
