using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json.Nodes;
using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

public sealed record CharacterAnimationPreview(IReadOnlyList<byte[]> Frames, double Duration);

public sealed class Character3DPreviewService
{
    private readonly Dictionary<string, byte[]> _staticCache = new(StringComparer.Ordinal);
    private readonly Queue<string> _staticCacheOrder = new();
    private readonly object _cacheLock = new();
    private int _cacheGeneration;
    public void ClearStaticCache() { lock (_cacheLock) { _cacheGeneration++; _staticCache.Clear(); _staticCacheOrder.Clear(); } }
    // 古い本体に未知の引数を渡して通常のゲームを起動しない。
    internal static bool IsSupported(string assemblyPath, bool animation = false)
    {
        if (!File.Exists(assemblyPath)) return false;
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata) return false;
        var metadata = pe.GetMetadataReader();
        return metadata.TypeDefinitions.Any(handle =>
        {
            var type = metadata.GetTypeDefinition(handle);
            return metadata.GetString(type.Name) == "DonModelPreview" && metadata.GetString(type.Namespace) == "TaikoDive"
                && (!animation || type.GetMethods().Any(m => metadata.GetString(metadata.GetMethodDefinition(m).Name) == "RenderAnimation"));
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
        cancellationToken.ThrowIfCancellationRequested();
        string key = installation.ExecutablePath + "\n" + CreateRequest(installation, settings);
        int generation;
        lock (_cacheLock)
        {
            if (_staticCache.TryGetValue(key, out var cached)) return cached;
            generation = _cacheGeneration;
        }
        byte[] png = (await RenderCoreAsync(installation, settings, null, cancellationToken)).Frames[0];
        cancellationToken.ThrowIfCancellationRequested();
        lock (_cacheLock)
        {
            if (generation == _cacheGeneration && !_staticCache.ContainsKey(key))
            {
                while (_staticCache.Count >= 8) _staticCache.Remove(_staticCacheOrder.Dequeue());
                _staticCache.Add(key, png); _staticCacheOrder.Enqueue(key);
            }
        }
        return png;
    }

    public Task<CharacterAnimationPreview> RenderAnimationAsync(TaikoDiveInstallation installation, Character3DSettings settings, string animation, CancellationToken cancellationToken) =>
        RenderCoreAsync(installation, settings, animation, cancellationToken);

    private async Task<CharacterAnimationPreview> RenderCoreAsync(TaikoDiveInstallation installation, Character3DSettings settings, string? animation, CancellationToken cancellationToken)
    {
        if (!IsSupported(Path.Combine(installation.BuildDirectory, "TaikoDive.dll"), animation is not null))
            throw new InvalidOperationException("3Dプレビュー対応版の TaikoDive に更新してください。");
        string directory = Path.Combine(Path.GetTempPath(), "TaikoDiveLauncher", "preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var process = new Process();
        int processId = 0;
        try
        {
            string request = Path.Combine(directory, "request.json"), output = Path.Combine(directory, "preview.png");
            var requestJson = JsonNode.Parse(CreateRequest(installation, settings))!.AsObject();
            if (animation is not null) requestJson["animation"] = animation;
            await File.WriteAllTextAsync(request, requestJson.ToJsonString(), timeout.Token);
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
            int count = 1; double duration = 1;
            if (animation is not null)
            {
                var manifest = JsonNode.Parse(await File.ReadAllTextAsync(output + ".animation.json", timeout.Token))!;
                count = (int)manifest["count"]!; duration = (double)manifest["duration"]!;
                if (count < 2 || count > 120 || !double.IsFinite(duration) || duration <= 0)
                    throw new InvalidDataException("アニメーションプレビューの形式が不正です。");
            }
            var frames = new List<byte[]>(count);
            for (int i = 0; i < count; i++)
                frames.Add(await File.ReadAllBytesAsync(i == 0 ? output : output + "." + i + ".png", timeout.Token));
            return new CharacterAnimationPreview(frames, duration);
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
