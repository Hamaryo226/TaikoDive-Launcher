using System.Text;
using System.Text.Json;
using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

public sealed partial class GameUpdateService
{
    private string CachedPackagePath(GameUpdateManifest update) => Path.Combine(GetUpdatesRoot(), "downloads", update.Sha256 + ".zip");
    private string HistoryPath => Path.Combine(GetUpdatesRoot(), "history.json");

    public async Task<UpdatePreview> PreviewAsync(CancellationToken token = default)
    {
        await _operationLock.WaitAsync(token).ConfigureAwait(false);
        string directory = Path.Combine(GetUpdatesRoot(), "staging", Guid.NewGuid().ToString("N"));
        try
        {
            var installation = _installationProvider() ?? throw new InvalidOperationException("ゲーム本体が見つかりません。");
            var update = AvailableUpdate ?? LatestUpdate ?? await FetchManifestAsync(token).ConfigureAwait(false);
            ValidateCompatibility(update);
            string key = _packageKeyProvider() ?? throw new InvalidOperationException("更新キーがありません。");
            SetState(GameUpdateState.Downloading, "更新内容を準備しています…", update, 0);
            string packagePath = CachedPackagePath(update);
            await DownloadAndVerifyAsync(packagePath, update, token).ConfigureAwait(false);
            var package = await _extractor.ExtractAndVerifyAsync(packagePath, directory, update, key, token).ConfigureAwait(false);
            var preview = await BuildPreviewAsync(installation, update, package, token).ConfigureAwait(false);
            SetState(GameUpdateState.Available, "更新内容を確認して適用してください。", update);
            return preview;
        }
        catch
        {
            SetState(GameUpdateState.Failed, "更新内容を準備できませんでした。再試行できます。", AvailableUpdate);
            throw;
        }
        finally { TryDeleteDirectory(directory); _operationLock.Release(); }
    }

    private void ValidateCompatibility(GameUpdateManifest update)
    {
        string ReadVersion(string channel)
        {
            string path = Path.Combine(Path.GetDirectoryName(GetUpdatesRoot())!, channel, "installed.json");
            if (!File.Exists(path)) return "0.0.0";
            var state = JsonSerializer.Deserialize(File.ReadAllText(path), LauncherJsonContext.Default.InstalledGameUpdate);
            return state is not null && _installationProvider() is { } installation && IsStateForInstallation(state, installation) ? state.Version : "0.0.0";
        }
        var problems = LauncherMaintenance.CheckCompatibility(update,
            _channel.StorageDirectoryName == "game-updates" ? update.Version : ReadVersion("game-updates"),
            _channel.StorageDirectoryName == "asset-updates" ? update.Version : ReadVersion("asset-updates"),
            LauncherBuildInfo.InformationalVersion);
        if (problems.Count > 0) throw new InvalidOperationException(string.Join("\n", problems));
    }

    private async Task<UpdatePreview> BuildPreviewAsync(TaikoDiveInstallation installation, GameUpdateManifest update,
        GamePackageManifest package, CancellationToken token)
    {
        var preview = new UpdatePreview { Version = update.Version, Revision = update.Revision, RequiredBytes = update.Size * 2 };
        foreach (var file in package.Files)
        {
            token.ThrowIfCancellationRequested();
            string relative = _channel.NormalizePath(file.Path);
            string path = GameUpdatePackageExtractor.ResolveContainedPath(installation.BuildDirectory, relative);
            bool exists = File.Exists(path);
            preview.RequiredBytes = checked(preview.RequiredBytes + file.Size + (exists ? new FileInfo(path).Length : 0));
            if (!exists) preview.Changes.Add(new(relative, "追加", file.Size));
            else if (new FileInfo(path).Length != file.Size || !file.Sha256.Equals(
                await GameUpdatePackageExtractor.ComputeSha256Async(path, token).ConfigureAwait(false), StringComparison.OrdinalIgnoreCase))
                preview.Changes.Add(new(relative, "変更", file.Size));
        }
        foreach (string path in FindUnlistedFiles(installation.BuildDirectory,
            package.Files.Select(f => _channel.NormalizePath(f.Path)).ToArray(), _channel.MirroredDirectories))
        {
            long size = new FileInfo(GameUpdatePackageExtractor.ResolveContainedPath(installation.BuildDirectory, path)).Length;
            preview.Changes.Add(new(path, "削除", size)); preview.RequiredBytes = checked(preview.RequiredBytes + size);
        }
        return preview;
    }

    public async Task<IReadOnlyList<UpdateHistoryEntry>> GetHistoryAsync()
    {
        var installation = _installationProvider();
        var history = await ReadHistoryAsync().ConfigureAwait(false);
        return history.Entries.Where(e => installation is not null && string.Equals(e.BuildDirectory, installation.BuildDirectory,
            StringComparison.OrdinalIgnoreCase)).OrderByDescending(e => e.InstalledAtUtc).ToArray();
    }

    private async Task<UpdateHistory> ReadHistoryAsync() => File.Exists(HistoryPath)
        ? JsonSerializer.Deserialize(await File.ReadAllTextAsync(HistoryPath).ConfigureAwait(false), LauncherJsonContext.Default.UpdateHistory)
            ?? throw new InvalidDataException("更新履歴が不正です。") : new();

    private Task WriteHistoryAsync(UpdateHistory history) => FilePersistence.WriteTextAtomicAsync(HistoryPath,
        JsonSerializer.Serialize(history, LauncherJsonContext.Default.UpdateHistory), new UTF8Encoding(false));

    public async Task<OperationResult> RestorePreviousAsync(string id, CancellationToken token = default)
    {
        await _operationLock.WaitAsync(token).ConfigureAwait(false);
        await ApplyLock.WaitAsync(token).ConfigureAwait(false);
        string? rescue = null;
        try
        {
            if (_isGameRunning()) throw new InvalidOperationException("TaikoDiveを終了してください。");
            var installation = _installationProvider() ?? throw new InvalidOperationException("ゲーム本体がありません。");
            var history = await ReadHistoryAsync().ConfigureAwait(false);
            var latest = history.Entries.Where(e => !e.Reverted && string.Equals(e.BuildDirectory, installation.BuildDirectory, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.InstalledAtUtc).FirstOrDefault();
            if (latest is null || latest.Id != id) throw new InvalidOperationException("この環境の最後の更新から順に戻してください。");
            string backupRoot = Path.GetFullPath(Path.Combine(GetUpdatesRoot(), "backups")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(latest.BackupDirectory).StartsWith(backupRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("バックアップの場所が不正です。");
            var installed = LoadInstalledUpdate();
            if (installed is null || installed.Version != latest.Version) throw new InvalidOperationException("適用済みバージョンが履歴と一致しません。");
            foreach (var change in latest.Changes)
            {
                string relative = _channel.NormalizePath(change.Path);
                if (change.Kind != "追加" && !File.Exists(GameUpdatePackageExtractor.ResolveContainedPath(latest.BackupDirectory, relative)))
                    throw new FileNotFoundException("復帰に必要なバックアップがありません: " + relative);
            }
            // 復帰前も全対象を退避する。途中で失敗したら現在の状態へ戻す。
            rescue = Path.Combine(GetUpdatesRoot(), "staging", Guid.NewGuid().ToString("N"));
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var change in latest.Changes)
            {
                string target = GameUpdatePackageExtractor.ResolveContainedPath(installation.BuildDirectory, _channel.NormalizePath(change.Path));
                if (!File.Exists(target)) continue;
                string saved = GameUpdatePackageExtractor.ResolveContainedPath(rescue, change.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(saved)!); File.Copy(target, saved);
                existing.Add(change.Path);
            }
            string? stateBefore = File.Exists(GetInstalledStatePath()) ? await File.ReadAllTextAsync(GetInstalledStatePath(), token).ConfigureAwait(false) : null;
            try
            {
                foreach (var change in latest.Changes)
                {
                    token.ThrowIfCancellationRequested();
                    if (_isGameRunning()) throw new InvalidOperationException("ゲームが起動したため復帰を中止しました。");
                    string target = GameUpdatePackageExtractor.ResolveContainedPath(installation.BuildDirectory, change.Path);
                    if (change.Kind == "追加") { if (File.Exists(target)) File.Delete(target); }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(GameUpdatePackageExtractor.ResolveContainedPath(latest.BackupDirectory, change.Path), target, true);
                    }
                }
                if (latest.PreviousInstalledState is null) TryDeleteFile(GetInstalledStatePath());
                else await File.WriteAllTextAsync(GetInstalledStatePath(), latest.PreviousInstalledState, token).ConfigureAwait(false);
                latest.Reverted = true;
                await WriteHistoryAsync(history).ConfigureAwait(false);
            }
            catch
            {
                foreach (var change in latest.Changes)
                {
                    string target = GameUpdatePackageExtractor.ResolveContainedPath(installation.BuildDirectory, change.Path);
                    if (existing.Contains(change.Path)) { Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(GameUpdatePackageExtractor.ResolveContainedPath(rescue, change.Path), target, true); }
                    else TryDeleteFile(target);
                }
                if (stateBefore is not null) await File.WriteAllTextAsync(GetInstalledStatePath(), stateBefore, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            SetState(GameUpdateState.Completed, "更新前のバージョンへ復帰しました。", null);
            return OperationResult.Success("更新前のバージョンへ復帰しました。");
        }
        catch (Exception ex) { return OperationResult.Failure(ex.Message); }
        finally { if (rescue is not null) TryDeleteDirectory(rescue); ApplyLock.Release(); _operationLock.Release(); }
    }
}
