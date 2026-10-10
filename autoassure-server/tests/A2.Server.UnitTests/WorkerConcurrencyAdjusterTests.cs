using A2.Server.Workers;

namespace A2.Server.UnitTests;

public sealed class WorkerConcurrencyAdjusterTests
{
    private static readonly WorkerConcurrencyOptions Options = new()
    {
        MinConcurrentMessages = 1,
        MaxConcurrentMessages = 8,
        CpuLowPercent = 60,
        CpuHighPercent = 85,
        MemoryLowPercent = 70,
        MemoryHighPercent = 85,
    };

    [Fact]
    public void ChooseNextLimit_WhenSpareCapacityAndAllSlotsBusy_AddsOne()
    {
        // test
        var softLimit = WorkerConcurrencyAdjuster.ChooseNextSoftLimit(
            4,
            4,
            30,
            40,
            Options
        );

        // verify
        Assert.Equal(5, softLimit);
    }

    [Fact]
    public void ChooseNextLimit_WhenSpareCapacityButSlotsIdle_KeepsSoftLimit()
    {
        // test
        var softLimit = WorkerConcurrencyAdjuster.ChooseNextSoftLimit(
            4,
            2,
            30,
            40,
            Options
        );

        // verify
        Assert.Equal(4, softLimit);
    }

    [Fact]
    public void ChooseNextLimit_WhenAtMax_DoesNotExceedMax()
    {
        // test
        var softLimit = WorkerConcurrencyAdjuster.ChooseNextSoftLimit(
            8,
            8,
            30,
            40,
            Options
        );

        // verify
        Assert.Equal(8, softLimit);
    }

    [Theory]
    [InlineData(90, 40)]
    [InlineData(30, 90)]
    public void ChooseNextLimit_WhenCpuOrMemoryHigh_CutsByAQuarter(
        double cpuPercent,
        double memoryPercent
    )
    {
        // test
        var softLimit = WorkerConcurrencyAdjuster.ChooseNextSoftLimit(
            8,
            8,
            cpuPercent,
            memoryPercent,
            Options
        );

        // verify
        Assert.Equal(6, softLimit);
    }

    [Fact]
    public void ChooseNextLimit_WhenStrainedWithSmallSoftLimit_CutsAtLeastOne()
    {
        // test
        var softLimit = WorkerConcurrencyAdjuster.ChooseNextSoftLimit(
            2,
            2,
            95,
            40,
            Options
        );

        // verify
        Assert.Equal(1, softLimit);
    }

    [Fact]
    public void ChooseNextLimit_WhenStrainedAtMin_StaysAtMin()
    {
        // test
        var softLimit = WorkerConcurrencyAdjuster.ChooseNextSoftLimit(
            1,
            1,
            95,
            95,
            Options
        );

        // verify
        Assert.Equal(1, softLimit);
    }

    [Fact]
    public void ChooseNextLimit_WhenBetweenLowAndHigh_KeepsSoftLimit()
    {
        // test
        var softLimit = WorkerConcurrencyAdjuster.ChooseNextSoftLimit(
            4,
            4,
            75,
            75,
            Options
        );

        // verify
        Assert.Equal(4, softLimit);
    }
}
