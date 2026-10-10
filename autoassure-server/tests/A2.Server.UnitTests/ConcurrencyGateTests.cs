using A2.Server.Workers;

namespace A2.Server.UnitTests;

public sealed class ConcurrencyGateTests
{
    [Fact]
    public async Task WaitForFreeSlotAsync_WhenSlotsAreFree_ReturnsFreeCount()
    {
        // setup
        var gate = new ConcurrencyGate(4);
        gate.TakeSlot();

        // test
        var freeSlots = await gate.WaitForFreeSlotAsync(CancellationToken.None);

        // verify
        Assert.Equal(3, freeSlots);
    }

    [Fact]
    public async Task WaitForFreeSlotAsync_WhenAllSlotsBusy_CompletesAfterRelease()
    {
        // setup
        var gate = new ConcurrencyGate(1);
        gate.TakeSlot();
        var waiting = gate.WaitForFreeSlotAsync(CancellationToken.None);
        Assert.False(waiting.IsCompleted);

        // test
        gate.ReleaseSlot();

        // verify
        Assert.Equal(1, await waiting.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task WaitForFreeSlotAsync_WhenSoftLimitRaised_CompletesWithoutRelease()
    {
        // setup
        var gate = new ConcurrencyGate(1);
        gate.TakeSlot();
        var waiting = gate.WaitForFreeSlotAsync(CancellationToken.None);

        // test
        gate.SetSoftLimit(3);

        // verify
        Assert.Equal(2, await waiting.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task WaitForFreeSlotAsync_WhenSoftLimitLoweredBelowInUse_WaitsUntilEnoughReleased()
    {
        // setup
        var gate = new ConcurrencyGate(3);
        gate.TakeSlot();
        gate.TakeSlot();
        gate.TakeSlot();
        gate.SetSoftLimit(1);
        var waiting = gate.WaitForFreeSlotAsync(CancellationToken.None);

        // test
        gate.ReleaseSlot();
        gate.ReleaseSlot();
        await Task.Delay(50);
        var completedWhileStillFull = waiting.IsCompleted;
        gate.ReleaseSlot();

        // verify
        Assert.False(completedWhileStillFull);
        Assert.Equal(1, await waiting.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task WaitForFreeSlotAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        // setup
        var gate = new ConcurrencyGate(1);
        gate.TakeSlot();
        using var cancellation = new CancellationTokenSource();
        var waiting = gate.WaitForFreeSlotAsync(cancellation.Token);

        // test
        await cancellation.CancelAsync();

        // verify
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }

    [Fact]
    public void ReleaseSlot_WhenNoSlotInUse_Throws()
    {
        // setup
        var gate = new ConcurrencyGate(2);

        // test + verify
        Assert.Throws<InvalidOperationException>(gate.ReleaseSlot);
    }
}
