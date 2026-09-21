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
        var settings = new Character3DSettings { ModelsPath = "Info/Chara/Models", Head = "52", Body = "4", UseCostume = true, Costume = "123", BodyColor = "#123456", LimbsColor = "#ABCDEF", FaceColor = "#112233", RimColor = "#445566" };
        var request = JsonNode.Parse(Character3DPreviewService.CreateRequest(installation, settings))!;
        Assert.AreEqual(Path.Combine(root, "Info", "Chara", "Models"), (string?)request["modelsPath"]);
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
}
