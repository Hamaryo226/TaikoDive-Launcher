using System.Text.Json.Nodes;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Tests;

[TestClass]
public sealed class Character3DTests
{
    [TestMethod] public async Task RestorePreviousLoadsUserAppearanceWithoutWritingCurrentSettings()
    {
        var store = new Character3DStore(() => false);
        await Assert.ThrowsAsync<FileNotFoundException>(() => store.LoadPreviousAsync(_installation, 1));
        await store.SaveAsync(_installation, new() { Head = "3", FaceColor = "#112233" }, 1);
        await store.SaveAsync(_installation, new() { Head = "4", FaceColor = "#445566" }, 1);
        string current = await File.ReadAllTextAsync(Character3DStore.SettingsPath(_installation));
        var previous = await store.LoadPreviousAsync(_installation, 1);
        Assert.AreEqual("3", previous.Head);
        Assert.AreEqual("#112233", previous.FaceColor);
        Assert.AreEqual(current, await File.ReadAllTextAsync(Character3DStore.SettingsPath(_installation)));
        Assert.AreEqual("4", (await store.LoadAsync(_installation, 1)).Head);
    }
    [TestMethod] public async Task UserSettingsAreIndependentAndFallbackToCommonValues()
    {
        var store = new Character3DStore(() => false);
        await store.SaveAsync(_installation, new() { BodyColor = "#123456", Head = "7" });
        var first = await store.LoadAsync(_installation, 1);
        Assert.AreEqual("#123456", first.BodyColor);
        first.Head = "3"; first.BodyColor = "#ABCDEF";
        first.ModelsPath = "ignored-for-user"; first.Enabled = true;
        await store.SaveAsync(_installation, first, 1);
        await store.SaveAsync(_installation, new() { UseCostume = true, Costume = "8", LimbsColor = "#FEDCBA" }, 9);
        var savedFirst = await store.LoadAsync(_installation, 1);
        var ninth = await store.LoadAsync(_installation, 9);
        var untouched = await store.LoadAsync(_installation, 2);
        Assert.AreEqual("3", savedFirst.Head); Assert.AreEqual("#ABCDEF", savedFirst.BodyColor);
        Assert.AreEqual("8", ninth.Costume); Assert.AreEqual("#FEDCBA", ninth.LimbsColor);
        Assert.AreEqual("7", untouched.Head); Assert.AreEqual("#123456", untouched.BodyColor);
        Assert.IsTrue(savedFirst.Enabled); Assert.AreEqual("Models/Donchan", savedFirst.ModelsPath);
        await store.SaveAsync(_installation, new() { BodyColor = "#101010" });
        Assert.AreEqual("#ABCDEF", (await store.LoadAsync(_installation, 1)).BodyColor);
        Assert.AreEqual("#101010", (await store.LoadAsync(_installation, 2)).BodyColor);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.LoadAsync(_installation, 10));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SaveAsync(_installation, new(), 0));
    }

    [TestMethod] public async Task PartialProfileInheritsDefaultsAndSaveKeepsOtherProfilesAndUnknownKeys()
    {
        string path = Character3DStore.SettingsPath(_installation);
        await File.WriteAllTextAsync(path, """
            {"parts":{"head":"6","body":"7"},"colors":{"body":"#112233","rim":"#445566"},
             "users":{"1":{"parts":{"head":"3","futurePart":42},"colors":{"body":"#ABCDEF","futureColor":"keep"},"future":true},
                      "2":{"parts":{"body":"9"},"future":[1,2]}}}
            """);
        var store = new Character3DStore(() => false);
        var first = await store.LoadAsync(_installation, 1);
        Assert.AreEqual("7", first.Body); Assert.AreEqual("#445566", first.RimColor);
        var before = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        await store.SaveAsync(_installation, first, 1);
        var after = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        Assert.IsTrue(JsonNode.DeepEquals(before["users"]!["2"], after["users"]!["2"]));
        Assert.AreEqual(42, (int)after["users"]!["1"]!["parts"]!["futurePart"]!);
        Assert.AreEqual("keep", (string?)after["users"]!["1"]!["colors"]!["futureColor"]);
        Assert.IsTrue((bool)after["users"]!["1"]!["future"]!);
        Assert.IsTrue(JsonNode.DeepEquals(before["parts"], after["parts"]));
    }

    private string _root = null!;
    private TaikoDiveInstallation _installation = null!;
    [TestInitialize] public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "TaikoDive-Character3D-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "Info"));
        _installation = new(_root);
        Asset("animations.glb"); Asset("face/face_000000.png");
        for (int i = 0; i <= 12; i++)
        {
            Asset($"head/{i}.glb"); Asset($"body/{i}.glb"); Asset($"cos/{i}.glb");
        }
    }
    [TestCleanup] public void Cleanup() => Directory.Delete(_root, true);
    private void Asset(string file)
    {
        string path = Path.Combine(_root, "Models", "Donchan", file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, [0]);
    }

    [TestMethod] public async Task RoundTripRetainsUnknownFieldsAndBackup()
    {
        string path = Character3DStore.SettingsPath(_installation);
        const string original = "{\"enabled\":false,\"future\":42,\"parts\":{\"futurePart\":3},\"colors\":{\"futureColor\":\"#123456\"}}";
        await File.WriteAllTextAsync(path, original);
        var store = new Character3DStore(() => false);
        var settings = await store.LoadAsync(_installation);
        settings.Body = "12"; settings.Head = "4"; settings.Costume = "5"; settings.UseCostume = true;
        settings.BodyColor = "#aabbcc"; settings.LimbsColor = "#012345"; settings.FaceColor = "#102030"; settings.RimColor = "#ABCDEF";
        await store.SaveAsync(_installation, settings);
        var loaded = await store.LoadAsync(_installation);
        Assert.AreEqual("#AABBCC", loaded.BodyColor); Assert.AreEqual(settings.LimbsColor, loaded.LimbsColor);
        Assert.AreEqual(settings.FaceColor, loaded.FaceColor); Assert.AreEqual(settings.RimColor, loaded.RimColor);
        Assert.AreEqual("12", loaded.Body); Assert.AreEqual("4", loaded.Head); Assert.AreEqual("5", loaded.Costume); Assert.IsTrue(loaded.UseCostume);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        Assert.AreEqual(42, (int)json["future"]!); Assert.AreEqual(3, (int)json["parts"]!["futurePart"]!);
        Assert.AreEqual("#123456", (string?)json["colors"]!["futureColor"]);
        Assert.AreEqual(original, await File.ReadAllTextAsync(path + ".launcher.bak"));
    }

    [TestMethod] public async Task EnabledModeValidatesOnlyItsSelectedParts()
    {
        Asset("animations.glb"); Asset("face/face_000000.png"); Asset("cos/2.glb");
        var store = new Character3DStore(() => false);
        var settings = new Character3DSettings { Enabled = true, UseCostume = true, Costume = "2", Head = "100", Body = "100" };
        await store.SaveAsync(_installation, settings);
        settings.UseCostume = false;
        await Assert.ThrowsAsync<FileNotFoundException>(() => store.SaveAsync(_installation, settings));
        Assert.IsTrue((await store.LoadAsync(_installation)).UseCostume);
        settings.Enabled = false;
        await Assert.ThrowsAsync<FileNotFoundException>(() => store.SaveAsync(_installation, settings));
    }

    [TestMethod] public async Task RunningGameAndInvalidInputDoNotOverwriteSettings()
    {
        string path = Character3DStore.SettingsPath(_installation); await File.WriteAllTextAsync(path, "{}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => new Character3DStore(() => true).SaveAsync(_installation, new()));
        await Assert.ThrowsAsync<InvalidDataException>(() => new Character3DStore(() => false).SaveAsync(_installation, new() { BodyColor = "red" }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new Character3DStore(() => false).SaveAsync(_installation, new() { Head = "../x" }));
        Assert.AreEqual("{}", await File.ReadAllTextAsync(path));
    }

    [TestMethod] public void CatalogUsesExistingFilesNamesAndMissingSelection()
    {
        Asset("head/0.glb"); Asset("head/3.glb"); Asset("head/invalid.glb");
        string root = Character3DStore.ModelRoot(_installation, "Models/Donchan");
        File.WriteAllText(Path.Combine(root, "costume_names.json"), "{\"head\":{\"3\":{\"name\":\"ねこ\"},\"4\":{\"name\":\"未導入\"}}}");
        var list = new Character3DStore(() => false).GetOptions(root, "head", "9");
        Assert.AreEqual(13, list.Count);
        StringAssert.Contains(list.Single(x => x.Id == "3").Label, "ねこ");
        var missing = new Character3DStore(() => false).GetOptions(root, "head", "99");
        StringAssert.Contains(missing[0].Label, "未配置");
    }
}
