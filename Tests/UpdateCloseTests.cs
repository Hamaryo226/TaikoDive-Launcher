using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Tests;

[TestClass]
[DoNotParallelize]
public sealed class UpdateCloseTests
{
    private string _directory = null!;
    private string? _previous;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "update-close-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _previous = Environment.GetEnvironmentVariable("TAIKODIVE_LAUNCHER_SETTINGS_DIRECTORY");
        Environment.SetEnvironmentVariable("TAIKODIVE_LAUNCHER_SETTINGS_DIRECTORY", _directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("TAIKODIVE_LAUNCHER_SETTINGS_DIRECTORY", _previous);
        Directory.Delete(_directory, true);
    }

    [TestMethod]
    [DataRow("cancel")]
    [DataRow("failed")]
    [DataRow("exception")]
    public async Task CancelOrInstallerFailureDoesNotPrepareOrExit(string outcome)
    {
        var context = new LauncherContext();
        await context.InitializeAsync();
        context.Preferences.Theme = "Light";
        using var cancellation = new CancellationTokenSource();
        int installers = 0, prepares = 0, exits = 0;
        Task<bool> Run() => context.PreferenceTransitions.RunUpdateAsync(
            () => Task.FromResult(outcome != "cancel"), context.RevertPreferenceEdits,
            () =>
            {
                installers++;
                Assert.IsFalse(context.HasUnsavedPreferenceEdits);
                if (outcome == "exception") throw new IOException("installer failed");
                return Task.FromResult(false);
            }, () =>
            {
                prepares++;
                return context.PrepareCloseAsync(cancellation, new(), context.PlacementGeneration);
            }, () => exits++);
        if (outcome == "exception") await Assert.ThrowsAsync<IOException>(Run);
        else Assert.AreEqual(outcome != "cancel", await Run());
        Assert.AreEqual(outcome == "cancel" ? 0 : 1, installers);
        Assert.AreEqual(outcome == "cancel", context.HasUnsavedPreferenceEdits);
        Assert.AreEqual(0, prepares);
        Assert.AreEqual(0, exits);
        Assert.IsFalse(cancellation.IsCancellationRequested);
        Assert.IsFalse(context.PreferenceTransitions.IsBusy);
    }

    [TestMethod]
    public async Task PlacementSaveFailureStillExitsAndPreservesProtection()
    {
        string path = Path.Combine(_directory, "launcher.json");
        await File.WriteAllTextAsync(path, "{}");
        var context = new LauncherContext();
        await context.InitializeAsync();
        int exits = 0;
        using var cancellation = new CancellationTokenSource();
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.IsTrue(await context.PreferenceTransitions.RunUpdateAsync(() => Task.FromResult(true),
                context.RevertPreferenceEdits, () => Task.FromResult(true),
                () => context.PrepareCloseAsync(cancellation, new() { X = 99 }, context.PlacementGeneration),
                () => exits++));
        }
        Assert.AreEqual(1, exits, "Existing close policy permits exit after a placement save failure");
        Assert.IsTrue(cancellation.IsCancellationRequested);
        Assert.IsFalse(context.CanSavePreferences);
        Assert.IsNull(context.Preferences.WindowPlacement);
        Assert.AreEqual("{}", await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UpdatePreparationRespectsProtectionAndPlacementGeneration(bool protectedSettings)
    {
        string path = Path.Combine(_directory, "launcher.json");
        string original = protectedSettings ? "{\"future\":42}" : "{}";
        await File.WriteAllTextAsync(path, original);
        var context = new LauncherContext();
        await context.InitializeAsync();
        int generation = context.PlacementGeneration;
        if (!protectedSettings) Assert.IsTrue(await context.ReloadPreferencesAsync());
        using var cancellation = new CancellationTokenSource();
        int exits = 0;
        await context.PreferenceTransitions.RunUpdateAsync(() => Task.FromResult(true), context.RevertPreferenceEdits,
            () => Task.FromResult(true), () => context.PrepareCloseAsync(cancellation, new() { X = 99 }, generation),
            () => exits++);
        Assert.AreEqual(1, exits);
        Assert.AreEqual(original, await File.ReadAllTextAsync(path));
        Assert.IsNull(context.Preferences.WindowPlacement);
        Assert.AreEqual(!protectedSettings, context.CanSavePreferences);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UpdateExitWaitsForPendingAndInflightPlacement(bool inFlight)
    {
        var context = new LauncherContext();
        await context.InitializeAsync();
        var coordinator = context.PreferenceTransitions;
        var installer = new TaskCompletionSource<bool>();
        var ioStarted = new TaskCompletionSource();
        var delay = new TaskCompletionSource(); // deterministic substitute for the 400ms timer
        using var cancellation = new CancellationTokenSource();
        var placement = new WindowPlacementPreferences { X = 42, Width = 900, Height = 700 };
        var path = Path.Combine(_directory, "launcher.json");
        using var lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Task delayedSave = SaveAfterDelay();
        async Task SaveAfterDelay()
        {
            try
            {
                await delay.Task.WaitAsync(cancellation.Token);
                var saving = context.SavePlacementAsync(placement, context.PlacementGeneration);
                ioStarted.SetResult();
                await saving;
            }
            catch (OperationCanceledException) { }
        }
        if (inFlight)
        {
            delay.SetResult();
            await ioStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsFalse(delayedSave.IsCompleted, "Real store I/O is waiting for the sidecar lease");
        }
        var latest = new WindowPlacementPreferences { X = 99, Width = 1000, Height = 750 };
        int exits = 0;
        Task<bool> update = coordinator.RunUpdateAsync(() => Task.FromResult(true), context.RevertPreferenceEdits,
            () => installer.Task,
            () => context.PrepareCloseAsync(cancellation, latest, context.PlacementGeneration), () =>
            {
                Assert.IsTrue(coordinator.IsBusy, "Ownership extends through the final exit");
                exits++;
            });
        try
        {
            Assert.IsTrue(coordinator.IsBusy);
            installer.SetResult(true);
            Assert.AreEqual(0, exits, "Installer success must not exit while latest placement is pending or in-flight.");
            Assert.IsFalse(update.IsCompleted);
            Assert.IsTrue(cancellation.IsCancellationRequested, "Flush cancels the pending debounce");
            Assert.IsFalse(await coordinator.RunAsync(() => throw new AssertFailedException("navigation consent"),
                () => Task.CompletedTask));
            Assert.IsFalse(await coordinator.RequestCloseAsync(() => false,
                () => throw new AssertFailedException("close consent"), () => { }, () => Task.CompletedTask, () => exits++));
            Assert.IsFalse(await coordinator.RunUpdateAsync(() => throw new AssertFailedException("second update"),
                () => { }, () => Task.FromResult(true), () => Task.CompletedTask, () => exits++));
        }
        finally
        {
            cancellation.Cancel();
            lease.Dispose();
            await delayedSave.WaitAsync(TimeSpan.FromSeconds(10));
            await update.WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.AreEqual(1, exits);
        Assert.IsFalse(coordinator.IsBusy);
        var saved = await new LauncherPreferencesStore().LoadAsync();
        Assert.IsNotNull(saved.WindowPlacement);
        Assert.AreEqual(99, saved.WindowPlacement.X, "Final placement, not the older in-flight one, must be persisted");
        Assert.AreEqual(1000, saved.WindowPlacement.Width);
    }
}
