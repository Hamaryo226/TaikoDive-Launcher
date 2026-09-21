using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json.Nodes;
using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

public sealed class Character3DPreviewService
{
    // 古い本体に未知の引数を渡して通常のゲームを起動しない。
    internal static bool IsSupported(string assemblyPath)
    {
        if (!File.Exists(assemblyPath)) return false;
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata) return false;
        var metadata = pe.GetMetadataReader();
        return metadata.TypeDefinitions.Any(handle =>
        {
            var type = metadata.GetTypeDefinition(handle);
            return metadata.GetString(type.Name) == "DonModelPreview" && metadata.GetString(type.Namespace) == "TaikoDive";
        });
    }

    internal static string CreateRequest(TaikoDiveInstallation installation, Character3DSettings settings) => new JsonObject
    {
        ["modelsPath"] = Character3DStore.ModelRoot(installation, settings.ModelsPath),
        ["parts"] = new JsonObject { ["useCostume"] = settings.UseCostume, ["head"] = settings.Head, ["body"] = settings.Body, ["costume"] = settings.Costume },
        ["colors"] = new JsonObject { ["body"] = settings.BodyColor, ["limbs"] = settings.LimbsColor, ["face"] = settings.FaceColor, ["rim"] = settings.RimColor },
    }.ToJsonString();

    public async Task<byte[]> RenderAsync(TaikoDiveInstallation installation, Character3DSettings settings, CancellationToken cancellationToken)
    {
        if (!IsSupported(Path.Combine(installation.BuildDirectory, "TaikoDive.dll")))
            throw new InvalidOperationException("3Dプレビュー対応版の TaikoDive に更新してください。");
        string directory = Path.Combine(Path.GetTempPath(), "TaikoDiveLauncher", "preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var process = new Process();
        int processId = 0;
        try
        {
            string request = Path.Combine(directory, "request.json"), output = Path.Combine(directory, "preview.png");
            await File.WriteAllTextAsync(request, CreateRequest(installation, settings), timeout.Token);
            process.StartInfo = new ProcessStartInfo(installation.ExecutablePath)
            {
                WorkingDirectory = directory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            process.StartInfo.ArgumentList.Add("--character-preview");
            process.StartInfo.ArgumentList.Add(request);
            process.StartInfo.ArgumentList.Add(output);
            if (!process.Start()) throw new InvalidOperationException("プレビューを開始できませんでした。");
            processId = process.Id;
            GameProcessService.PreviewProcesses.TryAdd(processId, 0);
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0 || !File.Exists(output))
            {
                string error = File.Exists(output + ".error.txt") ? await File.ReadAllTextAsync(output + ".error.txt", timeout.Token) : "モデルを描画できませんでした。";
                throw new InvalidOperationException(error);
            }
            return await File.ReadAllBytesAsync(output, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("プレビューの作成がタイムアウトしました。モデルフォルダーを確認してください。");
        }
        finally
        {
            if (processId != 0)
            {
                try { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
                finally { GameProcessService.PreviewProcesses.TryRemove(processId, out _); }
            }
            // 自分が作成した一意の一時フォルダーだけを削除する。
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
