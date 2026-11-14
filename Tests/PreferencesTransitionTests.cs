using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Tests;

[TestClass]
public sealed class PreferencesTransitionTests
{
    [TestMethod]
    public async Task CloseDuringNavigationConfirmationIsCancelledWithoutSecondDialog()
    {
        var coordinator = new PreferencesTransitionCoordinator();
        var answer = new TaskCompletionSource<bool>();
        bool dirty = true;
        int dialogs = 0, navigations = 0, closes = 0;
        var navigation = coordinator.RunAsync(() => { dialogs++; return answer.Task; },
            () => { dirty = false; navigations++; return Task.CompletedTask; });
        bool closed = await coordinator.RunAsync(() => { dialogs++; return Task.FromResult(true); },
            () => { closes++; return Task.CompletedTask; });
        Assert.IsFalse(closed);
        Assert.AreEqual(1, dialogs);
        Assert.IsTrue(dirty);
        answer.SetResult(false);
        Assert.IsFalse(await navigation);
        Assert.AreEqual(0, navigations);
        Assert.AreEqual(0, closes);
        Assert.IsTrue(dirty);
        Assert.IsFalse(coordinator.IsBusy);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DialogFailureCancelsAndKeepsEdits(bool asynchronous)
    {
        var coordinator = new PreferencesTransitionCoordinator();
        bool dirty = true;
        Assert.IsFalse(await coordinator.RunAsync(
            () => asynchronous ? Task.FromException<bool>(new InvalidOperationException("dialog busy"))
                : throw new InvalidOperationException("no XamlRoot"),
            () => { dirty = false; return Task.CompletedTask; }));
        Assert.IsTrue(dirty);
        Assert.IsFalse(coordinator.IsBusy);
        Assert.IsTrue(await coordinator.RunAsync(() => Task.FromResult(true),
            () => { dirty = false; return Task.CompletedTask; }));
        Assert.IsFalse(dirty);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UpdateRequiresConsentBeforeDiscardInstallerAndExit(bool consent)
    {
        var coordinator = new PreferencesTransitionCoordinator();
        bool dirty = true;
        int installers = 0, exits = 0;
        Func<Task<bool>> confirm = () => Task.FromResult(consent);
        Action discard = () => dirty = false;
        Func<Task<bool>> installer = () =>
        {
            Assert.IsTrue(coordinator.IsBusy, "Edits and other transitions stay blocked until installer returns");
            Assert.IsFalse(dirty);
            installers++;
            return Task.FromResult(true);
        };
        Action exit = () => exits++;
        await coordinator.RunUpdateAsync(confirm, discard, installer, () => Task.CompletedTask, exit);
        Assert.AreEqual(consent ? 1 : 0, installers);
        Assert.AreEqual(consent ? 1 : 0, exits);
        Assert.AreEqual(!consent, dirty);
        Assert.IsFalse(coordinator.IsBusy);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NavigationDuringCloseIsCancelledAndOnlyExplicitDiscardTransitions(bool discard)
    {
        var coordinator = new PreferencesTransitionCoordinator();
        var answer = new TaskCompletionSource<bool>();
        bool dirty = true;
        int closes = 0;
        var close = coordinator.RunAsync(() => answer.Task,
            () => { dirty = false; closes++; return Task.CompletedTask; });
        Assert.IsFalse(await coordinator.RunAsync(() => throw new AssertFailedException("Second dialog"),
            () => throw new AssertFailedException("Unexpected navigation")));
        answer.SetResult(discard);
        Assert.AreEqual(discard, await close);
        Assert.AreEqual(discard ? 1 : 0, closes);
        Assert.AreEqual(!discard, dirty);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InstallerLeaseRejectsDuplicateUpdateNavigationAndClose(bool succeeded)
    {
        var coordinator = new PreferencesTransitionCoordinator();
        var installer = new TaskCompletionSource<bool>();
        int exits = 0;
        var update = coordinator.RunUpdateAsync(() => Task.FromResult(true), () => { },
            () => installer.Task, () => Task.CompletedTask, () => exits++);
        Assert.IsTrue(coordinator.IsBusy);
        Assert.IsFalse(await coordinator.RunUpdateAsync(() => throw new AssertFailedException("Duplicate update"),
            () => Assert.Fail("Duplicate discard"), () => Task.FromResult(true), () => Task.CompletedTask, () => exits++));
        Assert.IsFalse(await coordinator.RunAsync(() => throw new AssertFailedException("Concurrent close/navigation"),
            () => Task.CompletedTask));
        installer.SetResult(succeeded);
        Assert.IsTrue(await update);
        Assert.AreEqual(succeeded ? 1 : 0, exits);
        Assert.IsFalse(coordinator.IsBusy);
    }

    [TestMethod]
    public async Task InstallerExceptionReleasesLeaseWithoutExit()
    {
        var coordinator = new PreferencesTransitionCoordinator();
        int exits = 0;
        await Assert.ThrowsAsync<IOException>(() => coordinator.RunUpdateAsync(() => Task.FromResult(true),
            () => { }, () => throw new IOException("installer failed"), () => Task.CompletedTask, () => exits++));
        Assert.AreEqual(0, exits);
        Assert.IsFalse(coordinator.IsBusy);
    }

    [TestMethod]
    public async Task ExplicitCloseRunsSharedConsentAndPreparation()
    {
        var coordinator = new PreferencesTransitionCoordinator();
        var calls = new List<string>();
        var result = coordinator.RequestCloseAsync(() => false,
            () => { calls.Add("confirm"); return Task.FromResult(true); },
            () => calls.Add("discard"),
            () => { calls.Add("prepare"); return Task.CompletedTask; },
            () => calls.Add("close"));
        Assert.IsTrue(await result);
        CollectionAssert.AreEqual(new[] { "confirm", "discard", "prepare", "close" }, calls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExplicitCloseCancelOrConsentKeepsOwnershipThroughPreparation(bool consent)
    {
        var coordinator = new PreferencesTransitionCoordinator();
        var answer = new TaskCompletionSource<bool>();
        var prepared = new TaskCompletionSource();
        bool dirty = true;
        int prepares = 0, closes = 0;
        Task<bool> Request() => coordinator.RequestCloseAsync(() => false, () => answer.Task,
            () => dirty = false, () => { prepares++; return prepared.Task; }, () =>
            {
                Assert.IsTrue(coordinator.IsCloseCommitted);
                Assert.IsTrue(coordinator.IsBusy);
                closes++;
                Assert.IsFalse(Request().GetAwaiter().GetResult(), "Reentrant final close");
            });
        var close = Request();
        Assert.IsFalse(await Request(), "Second close while confirming");
        Assert.IsTrue(dirty);
        answer.SetResult(consent);
        if (consent)
        {
            Assert.IsFalse(dirty);
            Assert.AreEqual(1, prepares);
            Assert.AreEqual(0, closes, "Close must await preparation");
            Assert.IsFalse(await Request(), "Second close while preparing");
            prepared.SetResult();
        }
        Assert.AreEqual(consent, await close);
        Assert.AreEqual(!consent, dirty);
        Assert.AreEqual(consent ? 1 : 0, closes);
        Assert.IsFalse(coordinator.IsBusy);
        if (consent) Assert.IsFalse(await Request(), "Duplicate after completion");
    }

    [TestMethod]
    public async Task ExplicitCloseRejectsLocalAndSharedBusyBeforeAsking()
    {
        var coordinator = new PreferencesTransitionCoordinator();
        Task<bool> Request(bool busy) => coordinator.RequestCloseAsync(() => busy,
            () => throw new AssertFailedException("Must not ask"),
            () => Assert.Fail("Must not discard"), () => throw new AssertFailedException("Must not prepare"),
            () => Assert.Fail("Must not close"));
        Assert.IsFalse(await Request(true));
        var answer = new TaskCompletionSource<bool>();
        var navigation = coordinator.RunAsync(() => answer.Task, () => Task.CompletedTask);
        Assert.IsFalse(await Request(false));
        answer.SetResult(false);
        await navigation;
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExplicitCloseFailureDoesNotLatchCommit(bool failDuringClose)
    {
        var coordinator = new PreferencesTransitionCoordinator();
        await Assert.ThrowsAsync<IOException>(() => coordinator.RequestCloseAsync(() => false,
            () => Task.FromResult(true), () => { },
            () => failDuringClose ? Task.CompletedTask : throw new IOException("prepare"),
            () => throw new IOException("close")));
        Assert.IsFalse(coordinator.IsCloseCommitted);
        Assert.IsFalse(coordinator.IsBusy);
        Assert.IsTrue(await coordinator.RequestCloseAsync(() => false, () => Task.FromResult(true),
            () => { }, () => Task.CompletedTask, () => { }));
    }

    [TestMethod]
    public async Task BusyNotificationReenablesControlsAfterExternalRefreshAndCancel()
    {
        var coordinator = new PreferencesTransitionCoordinator();
        var answer = new TaskCompletionSource<bool>();
        bool buttonsEnabled = true;
        var notifications = new List<bool>();
        void Refresh() => buttonsEnabled = !coordinator.IsBusy;
        EventHandler handler = (_, _) => { notifications.Add(coordinator.IsBusy); Refresh(); };
        coordinator.BusyChanged += handler;
        var navigation = coordinator.RunAsync(() => answer.Task, () => Task.CompletedTask);
        Refresh(); // OS motion or placement-save completion refreshes protection during the dialog.
        Assert.IsFalse(buttonsEnabled);
        answer.SetResult(false);
        Assert.IsFalse(await navigation);
        Assert.IsTrue(buttonsEnabled, "Cancel must reevaluate disabled buttons without another external notification.");
        CollectionAssert.AreEqual(new[] { true, false }, notifications);
        coordinator.BusyChanged -= handler;
        notifications.Clear();
        await coordinator.RunAsync(() => Task.FromResult(false), () => Task.CompletedTask);
        Assert.HasCount(0, notifications, "Unloaded page must not receive busy changes");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task BusyNotificationsAreBalancedOnFailure(bool transitionFails)
    {
        var coordinator = new PreferencesTransitionCoordinator();
        var states = new List<bool>();
        coordinator.BusyChanged += (_, _) => states.Add(coordinator.IsBusy);
        Task<bool> Run() => coordinator.RunAsync(
            () => transitionFails ? Task.FromResult(true) : throw new IOException("dialog"),
            () => throw new IOException("transition"));
        if (transitionFails) await Assert.ThrowsAsync<IOException>(Run);
        else Assert.IsFalse(await Run());
        CollectionAssert.AreEqual(new[] { true, false }, states);
        Assert.IsFalse(coordinator.IsBusy);
    }

    [TestMethod]
    public void SharedTransitionCoordinatorIsAvailable()
    {
        Assert.IsNotNull(typeof(LauncherContext).Assembly.GetType("TaikoDiveLauncher.Services.PreferencesTransitionCoordinator"),
            "Navigation, close and installer must use the same executable coordinator.");
    }
}
