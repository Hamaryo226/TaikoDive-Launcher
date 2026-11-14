namespace TaikoDiveLauncher.Services;

/// <summary>One UI-thread owner for confirmation and its destructive operation.</summary>
public sealed class PreferencesTransitionCoordinator
{
    public bool IsBusy { get; private set; }
    public event EventHandler? BusyChanged;
    public bool IsCloseCommitted { get; private set; }

    public Task<bool> RequestCloseAsync(Func<bool> operationBusy, Func<Task<bool>> confirm,
        Action discard, Func<Task> prepareClose, Action close)
        => RequestCloseAsync(operationBusy, confirm, discard, async finish => { await prepareClose(); finish(); }, close);

    public Task<bool> RequestCloseAsync(Func<bool> operationBusy, Func<Task<bool>> confirm,
        Action discard, Func<Action, Task> prepareClose, Action close)
    {
        if (IsCloseCommitted || operationBusy()) return Task.FromResult(false);
        return RunAsync(confirm, async () =>
        {
            discard();
            await prepareClose(() =>
            {
                // No await between the final placement check and committing Close.
                IsCloseCommitted = true;
                try { close(); }
                catch { IsCloseCommitted = false; throw; }
            });
        });
    }

    public Task<bool> RunUpdateAsync(Func<Task<bool>> confirm, Action discard,
        Func<Task<bool>> startInstaller, Func<Task> prepareClose, Action exit)
        => RunUpdateAsync(confirm, discard, startInstaller, async finish => { await prepareClose(); finish(); }, exit);

    public Task<bool> RunUpdateAsync(Func<Task<bool>> confirm, Action discard,
        Func<Task<bool>> startInstaller, Func<Action, Task> prepareClose, Action exit)
        => RunAsync(confirm, async () =>
        {
            discard();
            if (await startInstaller())
            {
                // Keep this update's ownership; RequestCloseAsync would reject our own busy state.
                await prepareClose(exit);
            }
        });

    public async Task<bool> RunAsync(Func<Task<bool>> confirm, Func<Task> transition)
    {
        if (IsBusy) return false;
        IsBusy = true;
        try
        {
            BusyChanged?.Invoke(this, EventArgs.Empty);
            // ContentDialog may fail synchronously or asynchronously (busy root/unloaded root).
            // Never interpret failure to ask as consent to discard.
            try { if (!await confirm()) return false; }
            catch (Exception) { return false; }
            await transition();
            return true;
        }
        finally
        {
            IsBusy = false;
            BusyChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
