namespace A2.Server.Workers;

// ReSharper disable once ClassNeverInstantiated.Global -- bound via IOptions<T> from configuration, not `new`'d directly
public record WorkerConcurrencyOptions
{
    public int MinConcurrentMessages { get; init; }
    public int MaxConcurrentMessages { get; init; }
    public int SampleIntervalSeconds { get; init; }
    public double CpuHighPercent { get; init; }
    public double CpuLowPercent { get; init; }
    public double MemoryHighPercent { get; init; }
    public double MemoryLowPercent { get; init; }

    public bool IsValid() =>
        MinConcurrentMessages >= 1
        && MaxConcurrentMessages >= MinConcurrentMessages
        && SampleIntervalSeconds >= 1
        && CpuLowPercent < CpuHighPercent
        && MemoryLowPercent < MemoryHighPercent;
}
