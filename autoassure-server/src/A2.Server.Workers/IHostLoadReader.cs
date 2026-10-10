namespace A2.Server.Workers;

public sealed record HostLoad
{
    public required double CpuUsedPercent { get; init; }
    public required double MemoryUsedPercent { get; init; }
}

/// <summary>
/// Reads how busy the host is, as a percentage of the container limit (or of the host when not in a container).
/// Real readings are only supported on Linux and Windows. On macOS use <see cref="FakeHostLoadReader" />.
/// </summary>
public interface IHostLoadReader
{
    /// <returns>The current load, or null when the host did not report CPU and memory use.</returns>
    HostLoad? ReadHostLoad();
}
