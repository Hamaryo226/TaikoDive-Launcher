using System.Text;
using System.Text.RegularExpressions;
using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

public sealed class UserProfileStore
{
    private const int MaximumProfiles = 9;

    static UserProfileStore()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<IReadOnlyList<UserProfile>> LoadAsync(TaikoDiveInstallation installation)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        HashSet<int> configuredSlots = [];

        if (File.Exists(installation.UserProfilePath))
        {
            byte[] bytes = await File.ReadAllBytesAsync(installation.UserProfilePath).ConfigureAwait(false);
            Encoding encoding = DetectEncoding(bytes);
            string content = encoding.GetString(RemovePreamble(bytes, encoding));

            foreach (string line in SplitLines(content))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('#'))
                {
                    continue;
                }

                int equalsIndex = line.IndexOf('=');
                if (equalsIndex < 0)
                {
                    continue;
                }

                string key = line[..equalsIndex].Trim();
                string value = line[(equalsIndex + 1)..].Trim();
                values[key] = value;

                Match slotMatch = Regex.Match(key, "^(?<slot>[1-9])P_User_", RegexOptions.IgnoreCase);
                if (slotMatch.Success)
                {
                    configuredSlots.Add(int.Parse(slotMatch.Groups["slot"].Value));
                }
            }
        }

        List<UserProfile> profiles = new(MaximumProfiles);
        for (int slot = 1; slot <= MaximumProfiles; slot++)
        {
            string prefix = $"{slot}P_User_";
            profiles.Add(new UserProfile
            {
                Slot = slot,
                Name = Get(values, prefix + "Name", "どんちゃん"),
                Title = Get(values, prefix + "Title", "ドンだーデビュー"),
                NamePlateType = ParseInteger(Get(values, prefix + "NamePlateType", "0")),
                CharaType = Get(values, prefix + "CharaType", "0"),
                IsConfigured = configuredSlots.Contains(slot),
            });
        }

        return profiles;
    }

    public async Task SaveAsync(TaikoDiveInstallation installation, UserProfile profile)
    {
        if (GameProcessService.IsRunning())
        {
            throw new InvalidOperationException("TaikoDive の実行中はプロフィールを保存できません。ゲームを終了してから再試行してください。");
        }

        if (profile.Slot is < 1 or > MaximumProfiles)
        {
            throw new ArgumentOutOfRangeException(nameof(profile), "ユーザー番号が範囲外です。");
        }

        ValidateValue(profile.Name, "名前", allowEmpty: false);
        ValidateValue(profile.Title, "称号", allowEmpty: true);
        ValidateValue(profile.CharaType, "キャラクター", allowEmpty: false);

        string content = string.Empty;
        Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        if (File.Exists(installation.UserProfilePath))
        {
            byte[] bytes = await File.ReadAllBytesAsync(installation.UserProfilePath).ConfigureAwait(false);
            encoding = DetectEncoding(bytes);
            content = encoding.GetString(RemovePreamble(bytes, encoding));
        }

        string newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string prefix = $"{profile.Slot}P_User_";
        content = SetValue(content, prefix + "Name", profile.Name.Trim(), newline);
        content = SetValue(content, prefix + "NamePlateType", Math.Max(0, profile.NamePlateType).ToString(), newline);
        content = SetValue(content, prefix + "Title", profile.Title.Trim(), newline);
        content = SetValue(content, prefix + "CharaType", profile.CharaType.Trim(), newline);

        await FilePersistence.WriteTextAtomicAsync(
            installation.UserProfilePath,
            content,
            encoding).ConfigureAwait(false);
    }

    public IReadOnlyList<StringOption> GetCharacterOptions(TaikoDiveInstallation installation, string? currentValue = null)
    {
        HashSet<string> values = new(StringComparer.OrdinalIgnoreCase);
        for (int type = 0; type <= 9; type++)
        {
            values.Add(type.ToString());
        }

        if (Directory.Exists(installation.CharacterDirectory))
        {
            foreach (string directory in Directory.EnumerateDirectories(installation.CharacterDirectory))
            {
                string name = Path.GetFileName(directory);
                if (!string.Equals(name, "AIDON", StringComparison.OrdinalIgnoreCase))
                {
                    values.Add(name);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(currentValue))
        {
            values.Add(currentValue);
        }

        return values
            .OrderBy(value => int.TryParse(value, out int number) ? number : int.MaxValue)
            .ThenBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Select(value => new StringOption(
                value,
                Directory.Exists(Path.Combine(installation.CharacterDirectory, value))
                    ? $"{value}  ·  利用可能"
                    : $"{value}  ·  フォルダーなし"))
            .ToList();
    }

    public IReadOnlyList<IntOption> GetNamePlateOptions(TaikoDiveInstallation installation, int currentValue = 0)
    {
        HashSet<int> values = [0, Math.Max(0, currentValue)];
        if (Directory.Exists(installation.NamePlateDirectory))
        {
            foreach (string directory in Directory.EnumerateDirectories(installation.NamePlateDirectory))
            {
                if (int.TryParse(Path.GetFileName(directory), out int type) && type >= 0)
                {
                    values.Add(type);
                }
            }
        }

        return values
            .Order()
            .Select(value => new IntOption(value, value == 0 ? "0  ·  標準" : value.ToString()))
            .ToList();
    }

    public Task<UserStatistics> GetStatisticsAsync(
        TaikoDiveInstallation installation,
        string userName,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            string folder = Path.Combine(installation.ScoreDirectory, SanitizeFileName(userName));
            if (!Directory.Exists(folder))
            {
                return new UserStatistics(0, 0, folder);
            }

            int scores = CountFiles(folder, "Score.dat", cancellationToken);
            int replays = CountFiles(folder, "best.json", cancellationToken);
            return new UserStatistics(scores, replays, folder);
        }, cancellationToken);
    }

    private static int CountFiles(string folder, string pattern, CancellationToken cancellationToken)
    {
        int count = 0;
        foreach (string _ in Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            count++;
        }

        return count;
    }

    private static string SetValue(string content, string key, string value, string newline)
    {
        Regex pattern = new(
            $"^(?<prefix>\\s*{Regex.Escape(key)}\\s*=\\s*)[^\\r\\n]*$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);

        if (pattern.IsMatch(content))
        {
            return pattern.Replace(content, match => match.Groups["prefix"].Value + value);
        }

        if (content.Length > 0 && !content.EndsWith("\r", StringComparison.Ordinal) && !content.EndsWith("\n", StringComparison.Ordinal))
        {
            content += newline;
        }

        return content + key + "=" + value + newline;
    }

    private static void ValidateValue(string? value, string fieldName, bool allowEmpty)
    {
        if (!allowEmpty && string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"{fieldName}を入力してください。");
        }

        if (value?.IndexOfAny(['\r', '\n', '=']) >= 0)
        {
            throw new InvalidDataException($"{fieldName}に改行または = は使用できません。");
        }
    }

    private static IEnumerable<string> SplitLines(string content) =>
        content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    private static string Get(IReadOnlyDictionary<string, string> values, string key, string fallback) =>
        values.TryGetValue(key, out string? value) ? value : fallback;

    private static int ParseInteger(string value) => int.TryParse(value, out int result) ? Math.Max(0, result) : 0;

    private static Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()))
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        }

        if (bytes.AsSpan().StartsWith(Encoding.Unicode.GetPreamble()))
        {
            return Encoding.Unicode;
        }

        if (bytes.AsSpan().StartsWith(Encoding.BigEndianUnicode.GetPreamble()))
        {
            return Encoding.BigEndianUnicode;
        }

        return Encoding.GetEncoding(932);
    }

    private static ReadOnlySpan<byte> RemovePreamble(byte[] bytes, Encoding encoding)
    {
        ReadOnlySpan<byte> preamble = encoding.GetPreamble();
        return preamble.Length > 0 && bytes.AsSpan().StartsWith(preamble)
            ? bytes.AsSpan(preamble.Length)
            : bytes;
    }

    private static string SanitizeFileName(string value)
    {
        string result = value;
        foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
        {
            result = result.Replace(invalidCharacter, '_');
        }

        return string.IsNullOrWhiteSpace(result) ? "Default" : result;
    }
}
