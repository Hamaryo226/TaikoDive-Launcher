using System.Collections.Concurrent;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Tests;

[TestClass]
[DoNotParallelize]
public sealed class PlacementDrainTests
{
    // A single-thread message pump, not a placement/debounce implementation.
    private sealed class UiPump : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback, object?)> _queue = new();
        public override void Post(SendOrPostCallback callback, object? state) => _queue.Add((callback, state));
        public static void Run(Func<Task> test)
        {
            var previous = Current;
            var pump = new UiPump();
            SetSynchronizationContext(pump);
            try
            {
                var task = test();
                while (!task.IsCompleted)
                {
                    if (!pump._queue.TryTake(out var work, TimeSpan.FromSeconds(15)))
                        throw new TimeoutException("UI test stalled");
                    work.Item1(work.Item2);
                }
                task.GetAwaiter().GetResult();
            }
            finally { SetSynchronizationContext(previous); }
        }
    }

    [TestMethod]
    [DataRow("generation")]
    [DataRow("protected")]
    [DataRow("failure")]
    public void DrainKeepsGenerationProtectionAndFailurePolicy(string mode) => UiPump.Run(async () =>
    {
        string directory = Path.Combine(Path.GetTempPath(), "placement-policy-" + Guid.NewGuid().ToString("N"));
        string? previous = Environment.GetEnvironmentVariable("TAIKODIVE_LAUNCHER_SETTINGS_DIRECTORY");
        Directory.CreateDirectory(directory);
        Environment.SetEnvironmentVariable("TAIKODIVE_LAUNCHER_SETTINGS_DIRECTORY", directory);
        string path = Path.Combine(directory, "launcher.json");
        string original = mode == "protected" ? "{\"future\":42}" : "{}";
        var release = new TaskCompletionSource();
        Task<bool>? closing = null;
        FileStream? locked = null;
        try
        {
            await File.WriteAllTextAsync(path, original);
            var context = new LauncherContext();
            await context.InitializeAsync();
            int oldGeneration = context.PlacementGeneration;
            int attempts = 0, exits = 0;
            var persistence = new WindowPlacementPersistence(async (placement, generation) =>
            {
                attempts++;
                await release.Task;
                await context.SavePlacementAsync(placement, generation);
            }, token => Task.Delay(Timeout.Infinite, token));
            persistence.Register(new() { X = 10 }, oldGeneration);
            closing = context.PreferenceTransitions.RunUpdateAsync(() => Task.FromResult(true),
                () => { }, () => Task.FromResult(true), persistence.PrepareCloseAsync, () => exits++);
            if (mode == "generation") Assert.IsTrue(await context.ReloadPreferencesAsync());
            if (mode == "failure") locked = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            // This late registration deliberately carries the old generation too.
            persistence.Register(new() { X = 20 }, oldGeneration);
            release.SetResult();
            Assert.IsTrue(await closing);
            locked?.Dispose();
            Assert.AreEqual(2, attempts);
            Assert.AreEqual(1, exits);
            Assert.AreEqual(original, await File.ReadAllTextAsync(path));
            Assert.AreEqual(mode == "generation", context.CanSavePreferences);
            Assert.IsNull(persistence.Pending);
        }
        finally
        {
            release.TrySetResult();
            locked?.Dispose();
            if (closing is not null) await closing;
            Environment.SetEnvironmentVariable("TAIKODIVE_LAUNCHER_SETTINGS_DIRECTORY", previous);
            Directory.Delete(directory, true);
        }
    });

    [TestMethod]
    public void CancelledTimerCannotSaveOldPlacementAndCloseFailureAllowsRetry() => UiPump.Run(async () =>
    {
        var timers = new List<TaskCompletionSource>();
        var tokens = new List<CancellationToken>();
        var saved = new List<int>();
        var persistence = new WindowPlacementPersistence((placement, _) =>
        {
            saved.Add(placement.X);
            return Task.CompletedTask;
        }, token =>
        {
            tokens.Add(token);
            var timer = new TaskCompletionSource();
            timers.Add(timer);
            return timer.Task; // Even if cancellation races with clock completion.
        });
        persistence.Register(new() { X = 1 }, 0);
        persistence.Register(new() { X = 2 }, 0);
        Assert.IsTrue(tokens[0].IsCancellationRequested);
        timers[0].SetResult();
        Assert.IsEmpty(saved);
        timers[1].SetResult();
        CollectionAssert.AreEqual(new[] { 2 }, saved);
        var coordinator = new PreferencesTransitionCoordinator();
        await Assert.ThrowsAsync<IOException>(() => coordinator.RequestCloseAsync(() => false,
            () => Task.FromResult(true), () => { }, persistence.PrepareCloseAsync, () => throw new IOException("close")));
        Assert.IsFalse(coordinator.IsCloseCommitted);
        Assert.IsTrue(persistence.Register(new() { X = 3 }, 0));
        int exits = 0;
        Assert.IsTrue(await coordinator.RequestCloseAsync(() => false, () => Task.FromResult(true),
            () => { }, persistence.PrepareCloseAsync, () => exits++));
        Assert.AreEqual(1, exits);
        Assert.IsTrue(tokens[2].IsCancellationRequested);
        timers[2].SetResult();
        CollectionAssert.AreEqual(new[] { 2, 3 }, saved);
    });

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void UpdateDrainsPlacementRegisteredDuringPreparation(bool inFlight, bool normalClose) => UiPump.Run(async () =>
    {
        string directory = Path.Combine(Path.GetTempPath(), "placement-drain-" + Guid.NewGuid().ToString("N"));
        string? previous = Environment.GetEnvironmentVariable("TAIKODIVE_LAUNCHER_SETTINGS_DIRECTORY");
        Directory.CreateDirectory(directory);
        Environment.SetEnvironmentVariable("TAIKODIVE_LAUNCHER_SETTINGS_DIRECTORY", directory);
        var releases = Enumerable.Range(0, 3).Select(_ => new TaskCompletionSource()).ToArray();
        var started = Enumerable.Range(0, 3).Select(_ => new TaskCompletionSource()).ToArray();
        var tokens = new List<CancellationToken>();
        var timer = new TaskCompletionSource();
        Task<bool>? update = null;
        try
        {
            var context = new LauncherContext();
            await context.InitializeAsync();
            int uiThread = Environment.CurrentManagedThreadId;
            var persistence = new WindowPlacementPersistence(async (placement, generation) =>
            {
                Assert.AreEqual(uiThread, Environment.CurrentManagedThreadId);
                started[placement.X].TrySetResult();
                await releases[placement.X].Task;
                await context.SavePlacementAsync(placement, generation);
            }, token => { tokens.Add(token); return timer.Task.WaitAsync(token); });
            var coordinator = context.PreferenceTransitions;
            var installer = new TaskCompletionSource<bool>();
            int exits = 0;
            persistence.Register(new() { X = 0 }, context.PlacementGeneration);
            if (inFlight)
            {
                timer.SetResult();
                await started[0].Task;
            }
            void Exit()
            {
                Assert.AreEqual(uiThread, Environment.CurrentManagedThreadId);
                Assert.IsTrue(coordinator.IsBusy);
                Assert.IsFalse(persistence.Register(new() { X = 99 }, context.PlacementGeneration),
                    "No registration is accepted between the final pending check and final Exit/Close");
                exits++;
            }
            update = normalClose
                ? coordinator.RequestCloseAsync(() => false, () => Task.FromResult(true),
                    context.RevertPreferenceEdits, persistence.PrepareCloseAsync, Exit)
                : coordinator.RunUpdateAsync(() => Task.FromResult(true), context.RevertPreferenceEdits,
                    () => installer.Task, persistence.PrepareCloseAsync, Exit);
            installer.SetResult(true);
            await started[0].Task;
            Assert.IsTrue(tokens[0].IsCancellationRequested);
            Assert.IsTrue(persistence.Register(new() { X = 1 }, context.PlacementGeneration));
            Assert.IsFalse(await coordinator.RunAsync(() => throw new AssertFailedException("navigation consent"),
                () => Task.CompletedTask));
            Assert.IsFalse(await coordinator.RequestCloseAsync(() => false,
                () => throw new AssertFailedException("close consent"), () => { }, persistence.PrepareCloseAsync, Exit));
            Assert.IsFalse(await coordinator.RunUpdateAsync(() => throw new AssertFailedException("update consent"),
                () => { }, () => Task.FromResult(true), persistence.PrepareCloseAsync, Exit));
            releases[0].SetResult();
            await Task.WhenAny(started[1].Task, update);
            Assert.AreEqual(0, exits, "A completion must not exit while B registered during preparation is unsaved");
            Assert.IsFalse(update.IsCompleted);
            persistence.Register(new() { X = 2 }, context.PlacementGeneration);
            releases[1].SetResult();
            await Task.WhenAny(started[2].Task, update);
            Assert.AreEqual(0, exits, "B completion must drain C too");
            releases[2].SetResult();
            Assert.IsTrue(await update);
            Assert.AreEqual(1, exits);
            Assert.HasCount(1, tokens, "During draining, registrations are drained instead of starting new timers");
            Assert.IsNull(persistence.Pending);
            Assert.AreEqual(2, (await new LauncherPreferencesStore().LoadAsync()).WindowPlacement!.X);
        }
        finally
        {
            foreach (var release in releases) release.TrySetResult();
            if (update is not null) await update;
            Environment.SetEnvironmentVariable("TAIKODIVE_LAUNCHER_SETTINGS_DIRECTORY", previous);
            Directory.Delete(directory, true);
        }
    });
}
