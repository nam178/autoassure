namespace A2.Server.Workers;

/// <summary>
/// Limits how many messages run at once, and lets the soft limit change while messages are running.
/// Lowering the soft limit never stops running messages; it only blocks new ones until enough finish.
/// The limit is soft: <see cref="TakeSlot" /> never refuses, so slots in use can briefly exceed it
/// (for example, when the limit is cut while the consumer waits on a queue poll).
/// Only one caller may take slots (the consumer loop). Any number of tasks may release them.
/// </summary>
public sealed class ConcurrencyGate(int initialSoftLimit)
{
    private readonly Lock _lock = new();
    private int _softLimit = initialSoftLimit;
    private int _slotsInUse;

    // the tasks that complete every time _softLimit or _slotInUse change.
    private TaskCompletionSource _changed = CreateSignal();

    public int SoftLimit
    {
        get
        {
            lock (_lock)
                return _softLimit;
        }
    }

    public int SlotsInUse
    {
        get
        {
            lock (_lock)
                return _slotsInUse;
        }
    }

    /// <returns>How many slots are free at the moment the wait ends. Always at least 1.</returns>
    /// <exception cref="OperationCanceledException">
    /// When <paramref name="cancellationToken" /> is cancelled.
    /// </exception>
    public async Task<int> WaitForFreeSlotAsync(
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            Task signal;
            lock (_lock)
            {
                if (_slotsInUse < _softLimit)
                    return _softLimit - _slotsInUse;

                signal = _changed.Task;
            }

            await signal.WaitAsync(cancellationToken);
        }
    }

    public void TakeSlot()
    {
        lock (_lock)
            _slotsInUse++;
    }

    /// <exception cref="InvalidOperationException">
    /// When no slot is in use.
    /// </exception>
    public void ReleaseSlot()
    {
        TaskCompletionSource previousSignal;
        lock (_lock)
        {
            if (_slotsInUse == 0)
                throw new InvalidOperationException(
                    "Released a slot that was never taken"
                );

            _slotsInUse--;
            previousSignal = ReplaceSignal();
        }

        previousSignal.SetResult();
    }

    public void SetSoftLimit(int newSoftLimit)
    {
        TaskCompletionSource previousSignal;
        lock (_lock)
        {
            _softLimit = newSoftLimit;
            previousSignal = ReplaceSignal();
        }

        previousSignal.SetResult();
    }

    private static TaskCompletionSource CreateSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private TaskCompletionSource ReplaceSignal()
    {
        var previousSignal = _changed;
        _changed = CreateSignal();
        return previousSignal;
    }
}
