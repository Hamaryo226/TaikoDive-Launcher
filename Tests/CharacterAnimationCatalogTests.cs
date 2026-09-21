using System.Text;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Tests;

[TestClass]
public sealed class CharacterAnimationCatalogTests
{
    [TestMethod]
    public void CatalogUsesActualClipNamesAndRejectsTruncatedGlb()
    {
        string directory = Path.Combine(Path.GetTempPath(), "TaikoDive-animation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "animations.glb");
        try
        {
            byte[] json = Encoding.UTF8.GetBytes("{\"animations\":[{\"name\":\"don_sabi\"},{\"name\":\"don_normal\"},{\"name\":\"don_sabi\"},{\"name\":\"\"}]}");
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(0x46546c67u); writer.Write(2u); writer.Write((uint)(20 + json.Length));
                writer.Write((uint)json.Length); writer.Write(0x4e4f534au); writer.Write(json);
            }
            CollectionAssert.AreEqual(new[] { "don_normal", "don_sabi" }, CharacterAnimationCatalog.Read(directory).ToArray());
            using (var file = File.OpenWrite(path)) file.SetLength(24);
            Assert.Throws<InvalidDataException>(() => CharacterAnimationCatalog.Read(directory));
        }
        finally { Directory.Delete(directory, true); }
    }
}
