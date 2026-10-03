using System.Text.Json.Nodes;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Tests;

[TestClass]
public class Character3DPreviewTests
{
    [TestMethod]
    public void PreviewUsesUnsavedAppearanceAndResolvesModelPathWithoutWritingSettings()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var installation = new TaikoDiveInstallation(root);
        var settings = new Character3DSettings { ModelsPath = "Info/Chara/Model", Head = "52", Body = "4", UseCostume = true, Costume = "123", BodyColor = "#123456", LimbsColor = "#ABCDEF", FaceColor = "#112233", RimColor = "#445566" };
        var request = JsonNode.Parse(Character3DPreviewService.CreateRequest(installation, settings))!;
        Assert.AreEqual(Path.Combine(root, "Info", "Chara", "Model"), (string?)request["modelsPath"]);
        Assert.AreEqual("52", (string?)request["parts"]!["head"]);
        Assert.AreEqual("123", (string?)request["parts"]!["costume"]);
        Assert.IsTrue((bool)request["parts"]!["useCostume"]!);
        Assert.AreEqual("#123456", (string?)request["colors"]!["body"]);
        Assert.AreEqual("#ABCDEF", (string?)request["colors"]!["limbs"]);
        Assert.AreEqual("#112233", (string?)request["colors"]!["face"]);
        Assert.AreEqual("#445566", (string?)request["colors"]!["rim"]);
        Assert.IsFalse(Directory.Exists(root));
    }

    [TestMethod]
    public void UnsupportedGameIsRejectedBeforeLaunching()
    {
        Assert.IsFalse(Character3DPreviewService.IsSupported(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dll")));
        Assert.IsFalse(Character3DPreviewService.IsSupported(typeof(Character3DPreviewTests).Assembly.Location));
    }

    [TestMethod]
    public void StaticPreviewIdentityRemainsStableUntilAppearanceChanges()
    {
        var installation = new TaikoDiveInstallation(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var settings = new Character3DSettings();
        string original = Character3DPreviewService.CreateStaticCacheKey(installation, settings);
        Assert.AreEqual(original, Character3DPreviewService.CreateStaticCacheKey(installation, settings with { }));
        Assert.AreNotEqual(original, Character3DPreviewService.CreateStaticCacheKey(installation, settings with { BodyColor = "#123456" }));
        Assert.AreNotEqual(original, Character3DPreviewService.CreateStaticCacheKey(installation, settings with { Head = "52" }));
        Assert.AreNotEqual(original, Character3DPreviewService.CreateStaticCacheKey(
            new TaikoDiveInstallation(installation.BuildDirectory + "-other"), settings));
    }

    [TestMethod]
    public void StaticPreviewInvalidatesWhenActiveAssetsOrRendererAreUpdated()
    {
        string root = Path.Combine(Path.GetTempPath(), "TaikoDiveLauncherPreviewTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var installation = new TaikoDiveInstallation(root);
            var settings = new Character3DSettings();
            string models = Character3DStore.ModelRoot(installation, settings.ModelsPath);
            Directory.CreateDirectory(Path.Combine(models, "head"));
            string original = Character3DPreviewService.CreateStaticCacheKey(installation, settings);
            File.WriteAllText(Path.Combine(models, "head", "1.glb"), "unused model");
            Assert.AreEqual(original, Character3DPreviewService.CreateStaticCacheKey(installation, settings));

            string model = Path.Combine(models, "head", "0.glb");
            File.WriteAllText(model, "active model");
            string available = Character3DPreviewService.CreateStaticCacheKey(installation, settings);
            Assert.AreNotEqual(original, available);
            File.SetLastWriteTimeUtc(model, File.GetLastWriteTimeUtc(model).AddSeconds(1));
            string updated = Character3DPreviewService.CreateStaticCacheKey(installation, settings);
            Assert.AreNotEqual(available, updated);

            string renderer = Path.Combine(root, "TaikoDive.dll");
            File.WriteAllText(renderer, "renderer");
            Assert.AreNotEqual(updated, Character3DPreviewService.CreateStaticCacheKey(installation, settings));
            string costumeKey = Character3DPreviewService.CreateStaticCacheKey(installation, settings with { UseCostume = true });
            File.WriteAllText(model, "head update does not affect costume");
            Assert.AreEqual(costumeKey, Character3DPreviewService.CreateStaticCacheKey(installation, settings with { UseCostume = true }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
