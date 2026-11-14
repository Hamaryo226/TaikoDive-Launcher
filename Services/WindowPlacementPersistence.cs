using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

// UI-thread owned: preserve the context across awaits, and invoke close in the
// same UI turn as the final pending check (no await/dispatch gap).
public sealed class WindowPlacementPersistence(
    Func<WindowPlacementPreferences, int, Task> save,
    Func<CancellationToken, Task>? delay = null)
{
    private CancellationTokenSource? _delayCancellation;
    private Task _inFlight = Task.CompletedTask;
    private int _generation;
    private bool _draining;
    private bool _closeCommitted;
    public WindowPlacementPreferences? Pending { get; private set; }

    public bool Register(WindowPlacementPreferences placement, int generation)
    {
        // Reject synchronous reentry from the final Close/Exit callback too.
        if (_closeCommitted) return false;
        Pending = placement;
        _generation = generation;
        CancelDelay();
        if (!_draining)
        {
            _delayCancellation = new();
            _ = SaveAfterDelayAsync(placement, generation, _delayCancellation.Token);
        }
        return true;
    }

    private void CancelDelay()
    {
        _delayCancellation?.Cancel();
        _delayCancellation?.Dispose();
        _delayCancellation = null;
    }

    private async Task SaveAfterDelayAsync(WindowPlacementPreferences placement, int generation, CancellationToken token)
    {
        try
        {
            await (delay?.Invoke(token) ?? Task.Delay(400, token));
            token.ThrowIfCancellationRequested();
            _inFlight = SaveAfterAsync(_inFlight, placement, generation);
            await _inFlight;
        }
        catch (OperationCanceledException) { }
    }

    private async Task SaveAfterAsync(Task previous, WindowPlacementPreferences placement, int generation)
    {
        await previous;
        try
        {
            await save(placement, generation);
            if (ReferenceEquals(Pending, placement)) Pending = null;
        }
        catch { /* Keep pending for a later retry; closing may still proceed. */ }
    }

    public async Task PrepareCloseAsync(Action close)
    {
        _draining = true;
        CancelDelay();
        try
        {
            await _inFlight;
            while (Pending is { } placement)
            {
                int generation = _generation;
                await SaveAfterAsync(Task.CompletedTask, placement, generation);
                // Attempt each latest registration once even on failure, without
                // clearing a newer registration or retrying a protected file forever.
                if (ReferenceEquals(Pending, placement)) Pending = null;
            }
            _closeCommitted = true;
            try { close(); }
            catch { _closeCommitted = false; throw; }
        }
        finally { _draining = false; }
    }
}
