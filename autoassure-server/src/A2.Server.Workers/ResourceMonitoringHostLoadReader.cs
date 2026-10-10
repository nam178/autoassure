using System.Diagnostics.Metrics;

namespace A2.Server.Workers;

/// <summary>
/// Reads CPU and memory use as a percentage of the container limit (or of the host when not in a container).
/// Works on Linux and Windows only. Needs AddResourceMonitoring() to be registered, otherwise nothing reports a value.
/// </summary>
public sealed class ResourceMonitoringHostLoadReader
    : IHostLoadReader,
        IDisposable
{
    private const string MeterName =
        "Microsoft.Extensions.Diagnostics.ResourceMonitoring";
    private const string CpuInstrumentName = "container.cpu.limit.utilization";
    private const string MemoryInstrumentName =
        "container.memory.limit.utilization";

    private readonly Lock _lock = new();
    private readonly MeterListener _listener = new();
    private double? _cpuRatio;
    private double? _memoryRatio;

    public ResourceMonitoringHostLoadReader()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            var isWanted =
                instrument.Meter.Name == MeterName
                && instrument.Name is CpuInstrumentName or MemoryInstrumentName;
            if (isWanted)
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<double>(
            (instrument, ratio, _, _) =>
            {
                lock (_lock)
                {
                    if (instrument.Name == CpuInstrumentName)
                        _cpuRatio = ratio;
                    else
                        _memoryRatio = ratio;
                }
            }
        );
        _listener.Start();
    }

    public HostLoad? ReadHostLoad()
    {
        lock (_lock)
        {
            _cpuRatio = null;
            _memoryRatio = null;
        }

        _listener.RecordObservableInstruments();

        lock (_lock)
        {
            if (
                _cpuRatio is not { } cpuRatio
                || _memoryRatio is not { } memoryRatio
            )
                return null;

            return new HostLoad
            {
                CpuUsedPercent = cpuRatio * 100,
                MemoryUsedPercent = memoryRatio * 100,
            };
        }
    }

    public void Dispose() => _listener.Dispose();
}
