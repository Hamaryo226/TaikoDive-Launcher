using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace TaikoDiveLauncher.Services;

internal static class ResumableDownload
{
    public static async Task FetchAsync(HttpClient client, Uri uri, string path, long size, string sha256,
        Action<long, long>? progress, CancellationToken token)
    {
        if (size <= 0 || !GameUpdatePackageExtractor.IsHex(sha256, 64)) throw new InvalidDataException("ダウンロード情報が不正です。");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (File.Exists(path))
        {
            if (await MatchesAsync(path, size, sha256, token)) { progress?.Invoke(size, size); return; }
            File.Delete(path);
        }
        string partial = path + ".partial";
        long offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (offset >= size)
        {
            if (offset == size && await MatchesAsync(partial, size, sha256, token)) { File.Move(partial, path, true); return; }
            File.Delete(partial); offset = 0;
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            var range = response.Content.Headers.ContentRange;
            if (range?.From != offset || range.To != size - 1 || range.Length != size)
                throw new InvalidDataException("再開位置が一致しません。ダウンロードを再試行してください。");
        }
        else if (response.StatusCode == HttpStatusCode.OK) offset = 0; // Range未対応なら最初から。
        else throw new InvalidDataException("予期しないダウンロード応答です。");
        if (response.Content.Headers.ContentLength is long length && length != size - offset)
            throw new InvalidDataException("更新サイズがマニフェストと一致しません。");
        await using (var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
        await using (var destination = new FileStream(partial, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            var buffer = new byte[81920]; long completed = offset;
            progress?.Invoke(completed, size);
            int count;
            while ((count = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                if (count > size - completed) throw new InvalidDataException("配信データが宣言サイズを超えています。");
                await destination.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                completed += count; progress?.Invoke(completed, size);
            }
            await destination.FlushAsync(token).ConfigureAwait(false);
        }
        if (!await MatchesAsync(partial, size, sha256, token).ConfigureAwait(false))
        {
            File.Delete(partial);
            throw new InvalidDataException("ダウンロードしたファイルのサイズまたはSHA-256が一致しません。");
        }
        File.Move(partial, path, true);
    }

    private static async Task<bool> MatchesAsync(string path, long size, string hash, CancellationToken token)
    {
        if (new FileInfo(path).Length != size) return false;
        await using var stream = File.OpenRead(path);
        return hash.Equals(Convert.ToHexString(await SHA256.HashDataAsync(stream, token)), StringComparison.OrdinalIgnoreCase);
    }
}
