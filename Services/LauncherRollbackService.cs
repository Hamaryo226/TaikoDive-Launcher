using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

public sealed partial class LauncherUpdateService
{
    private static string RollbackRoot => Path.Combine(LauncherMaintenance.UpdatesRoot, "launcher-backups");

    private async Task SaveRollbackBackupAsync(string target, LauncherUpdateManifest manifest, CancellationToken token)
    {
        var entry = new LauncherRollbackEntry { TargetPath = Path.GetFullPath(target), PreviousVersion = CurrentVersionText,
            PreviousSha256 = await ComputeSha256Async(target, token).ConfigureAwait(false), InstalledSha256 = manifest.Sha256, CreatedAtUtc = DateTime.UtcNow };
        string directory = Path.Combine(RollbackRoot, entry.Id); Directory.CreateDirectory(directory);
        File.Copy(target, Path.Combine(directory, ExecutableName));
        await FilePersistence.WriteTextAtomicAsync(Path.Combine(directory, "history.json"),
            JsonSerializer.Serialize(entry, LauncherJsonContext.Default.LauncherRollbackEntry), new UTF8Encoding(false), false).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LauncherRollbackEntry>> GetRollbackHistoryAsync()
    {
        var list = new List<LauncherRollbackEntry>();
        string? target = _processPathProvider();
        foreach (string path in LauncherToolkitStore.SafeFiles(RollbackRoot).Where(p => Path.GetFileName(p) == "history.json"))
        {
            try
            {
                var entry = JsonSerializer.Deserialize(await File.ReadAllTextAsync(path).ConfigureAwait(false), LauncherJsonContext.Default.LauncherRollbackEntry);
                if (entry is not null && string.Equals(entry.TargetPath, target, StringComparison.OrdinalIgnoreCase)) list.Add(entry);
            }
            catch (Exception ex) when (ex is IOException or JsonException) { }
        }
        return list.OrderByDescending(e => e.CreatedAtUtc).ToArray();
    }

    public async Task<OperationResult> RestoreLauncherAsync(string id, CancellationToken token = default)
    {
        if (!await _operationLock.WaitAsync(0, token).ConfigureAwait(false)) return OperationResult.Failure("アップデート処理が進行中です。");
        try
        {
            if (_isGameRunning()) throw new InvalidOperationException("ゲームを終了してください。");
            string target = _processPathProvider() ?? throw new InvalidOperationException("ランチャーを特定できません。");
            var entry = (await GetRollbackHistoryAsync().ConfigureAwait(false)).FirstOrDefault(e => e.Id == id)
                ?? throw new InvalidOperationException("復帰用の履歴がありません。");
            if (!Guid.TryParseExact(entry.Id, "N", out _)) throw new InvalidDataException("更新履歴のIDが不正です。");
            string backup = Path.Combine(RollbackRoot, entry.Id, ExecutableName);
            if (!File.Exists(backup) || !entry.PreviousSha256.Equals(await ComputeSha256Async(backup, token).ConfigureAwait(false), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("バックアップのSHA-256が一致しません。");
            if (!entry.InstalledSha256.Equals(await ComputeSha256Async(target, token).ConfigureAwait(false), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("現在のランチャーに対応する更新履歴から選択してください。");
            var start = new ProcessStartInfo(backup) { WorkingDirectory = Path.GetDirectoryName(target)!, UseShellExecute = false };
            foreach (string argument in new[] { "--apply-update", "--target", target, "--parent", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--working-directory", Path.GetDirectoryName(target)!, "--sha256", entry.PreviousSha256 }) start.ArgumentList.Add(argument);
            if (Process.Start(start) is null) throw new InvalidOperationException("復帰用のランチャーを起動できませんでした。");
            return OperationResult.Success("再起動して更新前のランチャーへ復帰します。");
        }
        catch (Exception ex) { return OperationResult.Failure(ex.Message); }
        finally { _operationLock.Release(); }
    }
}
