using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace A2.Server.Workers;

/// <summary>
/// Raises the message soft limit slowly while the host has spare CPU and memory, and cuts it fast when the host is strained.
/// Readings are relative to the container limit when running in a container.
/// </summary>
public sealed class WorkerConcurrencyAdjuster(
    ConcurrencyGate gate,
    IHostLoadReader hostLoadReader,
    IOptions<WorkerConcurrencyOptions> options,
    ILogger<WorkerConcurrencyAdjuster> logger
) : BackgroundService
{
    private static readonly TimeSpan MissingLoadWarningInterval =
        TimeSpan.FromMinutes(5);

    private TimeSpan _timeSinceMissingLoadWarning = MissingLoadWarningInterval;
    private bool _isLoadMissing;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sampleInterval = TimeSpan.FromSeconds(
            options.Value.SampleIntervalSeconds
        );
        using var timer = new PeriodicTimer(sampleInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                AdjustSoftLimit();
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // host is shutting down
        }
    }

    public static int ChooseNextSoftLimit(
        int currentSoftLimit,
        int slotsInUse,
        double cpuUsedPercent,
        double memoryUsedPercent,
        WorkerConcurrencyOptions options
    )
    {
        var isStrained =
            cpuUsedPercent >= options.CpuHighPercent
            || memoryUsedPercent >= options.MemoryHighPercent;
        if (isStrained)
        {
            var reducedSoftLimit = Math.Min(
                currentSoftLimit - 1,
                currentSoftLimit * 3 / 4
            );
            return Math.Max(options.MinConcurrentMessages, reducedSoftLimit);
        }

        var hasSpareCapacity =
            cpuUsedPercent < options.CpuLowPercent
            && memoryUsedPercent < options.MemoryLowPercent;
        var allSlotsBusy = slotsInUse >= currentSoftLimit;
        if (hasSpareCapacity && allSlotsBusy)
            return Math.Min(
                options.MaxConcurrentMessages,
                currentSoftLimit + 1
            );

        return currentSoftLimit;
    }

    private void WarnAboutMissingLoad()
    {
        _isLoadMissing = true;
        _timeSinceMissingLoadWarning += TimeSpan.FromSeconds(
            options.Value.SampleIntervalSeconds
        );
        if (_timeSinceMissingLoadWarning < MissingLoadWarningInterval)
            return;

        _timeSinceMissingLoadWarning = TimeSpan.Zero;
        logger.LogWarning(
            "CPU and memory readings are unavailable, so the message soft limit stays at {SoftLimit}. Is AddResourceMonitoring() registered, and is this host Linux or Windows? Warning again in at most {WarningIntervalMinutes} minutes",
            gate.SoftLimit,
            MissingLoadWarningInterval.TotalMinutes
        );
    }

    private void AdjustSoftLimit()
    {
        var hostLoad = hostLoadReader.ReadHostLoad();
        if (hostLoad is null)
        {
            WarnAboutMissingLoad();
            return;
        }

        if (_isLoadMissing)
        {
            _isLoadMissing = false;
            logger.LogInformation(
                "CPU and memory readings are available again"
            );
        }

        var currentSoftLimit = gate.SoftLimit;
        var slotsInUse = gate.SlotsInUse;
        var nextSoftLimit = ChooseNextSoftLimit(
            currentSoftLimit,
            slotsInUse,
            hostLoad.CpuUsedPercent,
            hostLoad.MemoryUsedPercent,
            options.Value
        );
        if (nextSoftLimit == currentSoftLimit)
            return;

        gate.SetSoftLimit(nextSoftLimit);
        logger.LogInformation(
            "Message soft limit {OldSoftLimit} -> {NewSoftLimit} (CPU {CpuPercent:F0}%, memory {MemoryPercent:F0}%, in use {SlotsInUse})",
            currentSoftLimit,
            nextSoftLimit,
            hostLoad.CpuUsedPercent,
            hostLoad.MemoryUsedPercent,
            slotsInUse
        );
    }
}
