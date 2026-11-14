using System.Xml.Linq;
namespace TaikoDiveLauncher.Tests;
[TestClass]
public sealed class LauncherRecoveryUiTests
{
    private static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaikoDiveLauncher.csproj"))) directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Repository not found");
        }
    }
    private static string Read(string path) => File.ReadAllText(Path.Combine(Root, path));
    [TestMethod]
    public void TransitionCallersShareOwnerAndUpdateBlocksPreferenceHandlers()
    {
        StringAssert.Contains(Read("MainPage.xaml.cs"), "Context.PreferenceTransitions");
        StringAssert.Contains(Read("MainWindow.xaml.cs"), "Context.PreferenceTransitions.RequestCloseAsync");
        string settings = Read("Pages/LauncherSettingsPage.xaml.cs");
        StringAssert.Contains(settings, "Context.PreferenceTransitions.RunUpdateAsync");
        StringAssert.Contains(settings, "App.MainWindow.PrepareCloseAsync, () => AppInstance.Exit()");
        StringAssert.Contains(Read("MainWindow.xaml.cs"), "internal Task PrepareCloseAsync(Action close) => _placementPersistence.PrepareCloseAsync(close)");
        StringAssert.Contains(settings, "public bool PreferencesBusy => _preferencesBusy || AppInstance.Context.PreferenceTransitions.IsBusy");
        StringAssert.Contains(settings, "変更を破棄してアップデート");
        StringAssert.Contains(settings, "if (_isLoading || PreferencesBusy ||");
        StringAssert.Contains(Read("Pages/LauncherSettingsPage.Recovery.cs"), "if (PreferencesBusy) return;");
    }

    [TestMethod]
    public void GameLaunchAutoExitUsesGuardedWindowClose()
    {
        string code = Read("Pages/HomePage.xaml.cs");
        Assert.DoesNotContain("AppInstance.Exit();", code);
        StringAssert.Contains(code, "await App.MainWindow.RequestCloseAsync();");
    }

    [TestMethod]
    public void WindowOnlyCapturesUserPlacementAndDoesNotCaptureOnClose()
    {
        string code = Read("MainWindow.xaml.cs");
        string closing = code.Split("AppWindow_Closing(AppWindow")[1].Split("private ")[0];
        Assert.DoesNotContain("CaptureWindowPlacement();", closing);
        StringAssert.Contains(code, "SavePlacementAsync");
        StringAssert.Contains(code, "WM_EXITSIZEMOVE");
        StringAssert.Contains(code, "OverlappedPresenterState.Minimized");
    }

    [TestMethod]
    public void MotionUsesSavedPreferenceOsPolicyVisibilityAndStartGuard()
    {
        string code = Read("Pages/HomePage.xaml.cs");
        StringAssert.Contains(code, "BackgroundAnimationEnabled");
        StringAssert.Contains(code, "AnimationsEnabledChanged");
        StringAssert.Contains(code, "if (effective == _animationRunning) return;");
        StringAssert.Contains(code, "_homeVisible");
        string settings = Read("Pages/LauncherSettingsPage.Recovery.cs");
        StringAssert.Contains(settings, "Windowsの設定により、背景アニメーションを停止しています");
        Assert.DoesNotContain("BackgroundAnimationEnabled =", code);
    }

    [TestMethod]
    public void RecoveryIsVisibleFromHomeAndNavigationConfirmsDiscard()
    {
        StringAssert.Contains(Read("MainPage.xaml"), "PreferencesProtectionBanner");
        StringAssert.Contains(Read("MainPage.xaml.cs"), "PreferencesStateChanged");
        StringAssert.Contains(Read("Pages/LauncherSettingsPage.xaml.cs"), "IUnsavedChangesAware");
        StringAssert.Contains(Read("Pages/LauncherSettingsPage.xaml.cs"), "RevertPreferenceEdits");
        StringAssert.Contains(Read("Pages/HomePage.xaml.cs"), "OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)");
    }

    [TestMethod]
    public void SharedSpacingAndActionsUseApprovedMinimumsAndContentWidth()
    {
        var theme = XDocument.Parse(Read("Styles/Theme.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var wide = theme.Descendants().Single(e => (string?)e.Attribute(x + "Key") == "PageWidePadding");
        Assert.AreEqual("32,24,32,40", wide.Value);
        Assert.AreEqual("16,16,16,24", theme.Descendants().Single(e => (string?)e.Attribute(x + "Key") == "PageNarrowPadding").Value);
        var action = theme.Descendants().Single(e => (string?)e.Attribute(x + "Key") == "ActionButtonStyle");
        Assert.AreEqual("48", (string?)action.Elements().Single(e => (string?)e.Attribute("Property") == "MinHeight").Attribute("Value"));
        Assert.AreEqual("16,8", (string?)action.Elements().Single(e => (string?)e.Attribute("Property") == "Padding").Attribute("Value"));
        StringAssert.Contains(Read("Pages/HomePage.xaml"), "MinHeight=\"56\"");
        Assert.DoesNotContain("MinWindowWidth", Read("Pages/LauncherSettingsPage.xaml"));
        StringAssert.Contains(Read("Pages/LauncherSettingsPage.Recovery.cs"), "e.NewSize.Width");
    }

    [TestMethod]
    public void ClosingUnsavedPreferencesRequiresCancelableConfirmation()
    {
        string code = Read("MainWindow.xaml.cs");
        StringAssert.Contains(code, "HasUnsavedPreferenceEdits");
        StringAssert.Contains(code, "args.Cancel = true");
        StringAssert.Contains(code, "DefaultButton = ContentDialogButton.Close");
        StringAssert.Contains(code, "未保存の変更があります。終了しますか？");
    }

    [TestMethod]
    public void SettingsControlsObserveProtectionChangesFromPlacementSaving()
    {
        string code = Read("Pages/LauncherSettingsPage.xaml.cs");
        StringAssert.Contains(code, "Context.PreferenceTransitions.BusyChanged += PreferenceTransitions_BusyChanged");
        StringAssert.Contains(code, "Context.PreferenceTransitions.BusyChanged -= PreferenceTransitions_BusyChanged");
        StringAssert.Contains(code, "PreferenceTransitions_BusyChanged(object? sender, EventArgs e) => UpdatePreferencesProtection()");
        StringAssert.Contains(code, "Context.PreferencesStateChanged += PreferencesStateChanged");
        StringAssert.Contains(code, "Context.PreferencesStateChanged -= PreferencesStateChanged");
        Assert.DoesNotContain("保存済みの設定", code); // first-run defaults are not persisted
    }

    [TestMethod]
    public void SettingsRecoveryActionsAreWiredAndProtectedEditsAreNotRolledBack()
    {
        var xml = XDocument.Parse(Read("Pages/LauncherSettingsPage.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (string name in new[] { "ReloadPreferencesButton", "ResetPreferencesButton", "RetryPreferencesButton", "RevertPreferencesButton", "PreferencesProtectionBar" })
            Assert.IsTrue(xml.Descendants().Any(e => (string?)e.Attribute(x + "Name") == name), name);
        string code = Read("Pages/LauncherSettingsPage.xaml.cs") + Read("Pages/LauncherSettingsPage.Recovery.cs");
        StringAssert.Contains(code, "未保存の変更があります。設定を読み直しますか？");
        StringAssert.Contains(code, "DefaultButton = ContentDialogButton.Close");
        Assert.DoesNotContain("Preferences.Theme = previousTheme;", code);
        Assert.DoesNotContain("Preferences.CloseAfterLaunch = previousValue;", code);
        Assert.DoesNotContain("Preferences.HomeBannerStyle = previous;", code);
        StringAssert.Contains(code, "設定ファイルを手動で編集する場合は、ランチャーをすべて終了してから行ってください。");
    }
}
