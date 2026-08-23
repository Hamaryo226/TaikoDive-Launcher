using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Readers;
using TaikoDiveLauncher.Models;
using PayloadZipArchive = System.IO.Compression.ZipArchive;
using PayloadZipArchiveEntry = System.IO.Compression.ZipArchiveEntry;
using SharpZipArchive = SharpCompress.Archives.Zip.ZipArchive;

namespace TaikoDiveLauncher.Services;

internal static partial class GameUpdatePackageNaming
{
    [GeneratedRegex(@"^TaikoDive_Update_v(?<version>(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*))_win-x64_(?<revision>[0-9a-f]{7})\.zip$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageNameRegex();

    [GeneratedRegex(@"^(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();

    public static string Create(string version, string revision)
    {
        if (!IsVersion(version) || !IsRevision(revision))
        {
            throw new ArgumentException("バージョンまたはリビジョンが不正です。");
        }

        return $"TaikoDive_Update_v{version}_win-x64_{revision[..7].ToLowerInvariant()}.zip";
    }

    public static bool Matches(string fileName, string version, string revision)
    {
        Match match = PackageNameRegex().Match(fileName);
        return match.Success
            && string.Equals(match.Groups["version"].Value, version, StringComparison.Ordinal)
            && revision.Length >= 7
            && string.Equals(match.Groups["revision"].Value, revision[..7], StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsVersion(string value) => VersionRegex().IsMatch(value)
        && value.Split('.').All(part => int.TryParse(part, out _));

    public static bool IsRevision(string value) => value.Length == 40 && value.All(Uri.IsHexDigit);

    public static int CompareVersions(string left, string right)
    {
        if (!IsVersion(left) || !IsVersion(right))
        {
            throw new ArgumentException("セマンティックバージョンが不正です。");
        }

        int[] leftParts = left.Split('.').Select(int.Parse).ToArray();
        int[] rightParts = right.Split('.').Select(int.Parse).ToArray();
        for (int index = 0; index < leftParts.Length; index++)
        {
            int comparison = leftParts[index].CompareTo(rightParts[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }
}

internal static class GamePackageKeyProvider
{
    public static string KeyId => GetMetadata("TaikoDiveGamePackageKeyId") ?? "2026-01";

    public static string? GetPackageKey()
    {
        string? developmentOverride = Environment.GetEnvironmentVariable("TAIKODIVE_GAME_PACKAGE_KEY");
        return string.IsNullOrWhiteSpace(developmentOverride)
            ? GetMetadata("TaikoDiveGamePackageKey")
            : developmentOverride;
    }

    public static string DerivePassword(string packageKey, string packageFileName)
    {
        if (string.IsNullOrWhiteSpace(packageKey))
        {
            throw new InvalidOperationException("ゲーム更新用キーがランチャーに設定されていません。");
        }

        using HMACSHA256 hmac = new(Encoding.UTF8.GetBytes(packageKey));
        byte[] digest = hmac.ComputeHash(Encoding.UTF8.GetBytes(packageFileName));
        return Convert.ToBase64String(digest);
    }

    private static string? GetMetadata(string key) => Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))
        ?.Value;
}

internal static class GameUpdatePathPolicy
{
    private static readonly string[] ProtectedFiles =
    [
        "Setting.json",
        "Info/User.ini",
        "TaikoDive.Launcher.exe",
        "Log.txt",
    ];

    private static readonly string[] ProtectedDirectories =
    [
        "Songs/",
        "Info/ScoreData/",
        "Info/TaikoDiveLauncher/",
        "Replay/",
        "Replays/",
        "Screenshot/",
        "Screenshots/",
        "Log/",
    ];

    public static string NormalizeAndValidate(string path)
    {
        string normalized = path.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized)
            || Path.IsPathRooted(path)
            || normalized.Contains(':', StringComparison.Ordinal)
            || normalized.Split('/').Any(segment =>
                segment is "" or "." or ".."
                || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || segment.EndsWith(' ')
                || segment.EndsWith('.')))
        {
            throw new InvalidDataException($"更新パッケージに不正なパスがあります: {path}");
        }

        if (ProtectedFiles.Contains(normalized, StringComparer.OrdinalIgnoreCase)
            || ProtectedDirectories.Any(prefix => normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            || normalized.EndsWith(".launcher.bak", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"更新パッケージに保護対象のユーザーファイルが含まれています: {normalized}");
        }

        return normalized;
    }
}

internal sealed class GameUpdatePackageExtractor
{
    private const string PackageManifestName = "package-files.json";
    private const int MaximumEntryCount = 20_000;
    private const long MaximumEntrySize = 2L * 1024 * 1024 * 1024;
    private const long MaximumExpandedSize = 8L * 1024 * 1024 * 1024;

    public async Task<GamePackageManifest> ExtractAndVerifyAsync(
        string packagePath,
        string stagingDirectory,
        GameUpdateManifest update,
        string packageKey,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(stagingDirectory);
        string payloadPath = Path.Combine(stagingDirectory, "payload.bin");
        string password = GamePackageKeyProvider.DerivePassword(packageKey, update.PackageFileName);
        ExtractEncryptedPayload(packagePath, payloadPath, password, update.Archive.Payload);

        string filesDirectory = Path.Combine(stagingDirectory, "files");
        Directory.CreateDirectory(filesDirectory);
        GamePackageManifest packageManifest = await ExtractPayloadAsync(
            payloadPath,
            filesDirectory,
            cancellationToken).ConfigureAwait(false);
        ValidatePackageManifest(packageManifest, update);
        await VerifyExtractedFilesAsync(filesDirectory, packageManifest, cancellationToken).ConfigureAwait(false);
        return packageManifest;
    }

    private static void ExtractEncryptedPayload(
        string packagePath,
        string payloadPath,
        string password,
        string expectedPayloadName)
    {
        ReaderOptions options = new() { Password = password };
        using IArchive archive = SharpZipArchive.OpenArchive(packagePath, options);
        var files = archive.Entries.Where(entry => !entry.IsDirectory).ToList();
        if (files.Count != 1
            || !string.Equals(files[0].Key?.Replace('\\', '/'), expectedPayloadName, StringComparison.Ordinal)
            || !files[0].IsEncrypted
            || files[0].Size is <= 0 or > MaximumExpandedSize)
        {
            throw new InvalidDataException("暗号化ZIPは、暗号化されたpayload.binを1つだけ含む必要があります。");
        }

        using Stream source = files[0].OpenEntryStream();
        using FileStream destination = new(payloadPath, FileMode.Create, FileAccess.Write, FileShare.None);
        CopyWithLimit(source, destination, MaximumExpandedSize);
    }

    private static async Task<GamePackageManifest> ExtractPayloadAsync(
        string payloadPath,
        string filesDirectory,
        CancellationToken cancellationToken)
    {
        using FileStream payload = new(payloadPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using PayloadZipArchive archive = new(payload, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count is 0 or > MaximumEntryCount)
        {
            throw new InvalidDataException("更新パッケージのファイル数が許容範囲外です。");
        }

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        long expandedSize = 0;
        GamePackageManifest? packageManifest = null;
        foreach (PayloadZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string entryPath = entry.FullName.Replace('\\', '/');
            if (entryPath.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            if (!seen.Add(entryPath))
            {
                throw new InvalidDataException($"更新パッケージ内でパスが重複しています: {entryPath}");
            }

            if (entry.Length < 0 || entry.Length > MaximumEntrySize
                || expandedSize > MaximumExpandedSize - entry.Length)
            {
                throw new InvalidDataException("更新パッケージの展開サイズが上限を超えています。");
            }
            expandedSize += entry.Length;

            if (string.Equals(entryPath, PackageManifestName, StringComparison.Ordinal))
            {
                await using Stream manifestStream = entry.Open();
                packageManifest = await JsonSerializer.DeserializeAsync(
                        manifestStream,
                        LauncherJsonContext.Default.GamePackageManifest,
                        cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            string normalized = GameUpdatePathPolicy.NormalizeAndValidate(entryPath);
            string destinationPath = ResolveContainedPath(filesDirectory, normalized);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using Stream source = entry.Open();
            await using FileStream destination = new(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await CopyWithLimitAsync(source, destination, entry.Length, cancellationToken).ConfigureAwait(false);
        }

        return packageManifest ?? throw new InvalidDataException("package-files.jsonがありません。");
    }

    private static void ValidatePackageManifest(GamePackageManifest package, GameUpdateManifest update)
    {
        if (!string.Equals(package.Version, update.Version, StringComparison.Ordinal)
            || !string.Equals(package.Revision, update.Revision, StringComparison.OrdinalIgnoreCase)
            || package.Files.Count is 0 or > MaximumEntryCount)
        {
            throw new InvalidDataException("内部マニフェストが外部マニフェストと一致しません。");
        }

        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach (GamePackageFile file in package.Files)
        {
            file.Path = GameUpdatePathPolicy.NormalizeAndValidate(file.Path);
            if (!paths.Add(file.Path)
                || file.Size is < 0 or > MaximumEntrySize
                || !IsHex(file.Sha256, 64))
            {
                throw new InvalidDataException("内部マニフェストのファイル情報が不正です。");
            }
            file.Sha256 = file.Sha256.ToUpperInvariant();
        }
    }

    private static async Task VerifyExtractedFilesAsync(
        string filesDirectory,
        GamePackageManifest package,
        CancellationToken cancellationToken)
    {
        HashSet<string> expected = package.Files.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] actual = Directory.GetFiles(filesDirectory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(filesDirectory, path).Replace('\\', '/'))
            .ToArray();
        if (actual.Length != expected.Count || actual.Any(path => !expected.Contains(path)))
        {
            throw new InvalidDataException("内部マニフェストとパッケージ内のファイル一覧が一致しません。");
        }

        foreach (GamePackageFile file in package.Files)
        {
            string path = ResolveContainedPath(filesDirectory, file.Path);
            FileInfo info = new(path);
            if (!info.Exists || info.Length != file.Size)
            {
                throw new InvalidDataException($"ファイルサイズが一致しません: {file.Path}");
            }

            string hash = await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"SHA-256が一致しません: {file.Path}");
            }
        }
    }

    internal static string ResolveContainedPath(string root, string relativePath)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"更新パッケージのパスが展開先の外を指しています: {relativePath}");
        }
        return candidate;
    }

    internal static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
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

    internal static bool IsHex(string? value, int length) => value?.Length == length && value.All(Uri.IsHexDigit);

    private static void CopyWithLimit(Stream source, Stream destination, long maximumBytes)
    {
        byte[] buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (total > maximumBytes - read)
            {
                throw new InvalidDataException("更新パッケージの展開サイズが上限を超えています。");
            }
            destination.Write(buffer, 0, read);
            total += read;
        }
    }

    private static async Task CopyWithLimitAsync(
        Stream source,
        Stream destination,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (total > maximumBytes - read)
            {
                throw new InvalidDataException("更新パッケージ内のファイルサイズが宣言値を超えています。");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            total += read;
        }
    }
}
