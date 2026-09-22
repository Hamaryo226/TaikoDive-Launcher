using System.Text;
using System.Text.Json.Nodes;

namespace TaikoDiveLauncher.Services;

public static class CharacterAnimationCatalog
{
    public static IReadOnlyList<string> Read(string modelRoot)
    {
        using var reader = new BinaryReader(File.OpenRead(Path.Combine(modelRoot, "animations.glb")));
        if (reader.ReadUInt32() != 0x46546c67 || reader.ReadUInt32() != 2)
            throw new InvalidDataException("GLB 2.0 のアニメーションが必要です。");
        uint length = reader.ReadUInt32();
        if (length != reader.BaseStream.Length) throw new InvalidDataException("GLBの長さが不正です。");
        int size = checked((int)reader.ReadUInt32());
        if (reader.ReadUInt32() != 0x4e4f534a || size < 0 || size > 64 * 1024 * 1024 || size > length - 20)
            throw new InvalidDataException("GLBのJSONチャンクが不正です。");
        var json = JsonNode.Parse(Encoding.UTF8.GetString(reader.ReadBytes(size)));
        var names = (json?["animations"] as JsonArray)?.Select(a => (string?)a?["name"])
            .Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() ?? [];
        if (names.Length == 0) throw new InvalidDataException("アニメーションがありません。");
        return names;
    }
}
