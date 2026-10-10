namespace A2.Server.Workers;

/// <summary>
/// Never reports a load, so the message soft limit stays at the value from app settings.
/// For running the worker locally on macOS, where real CPU and memory readings are not supported.
/// </summary>
public sealed class FakeHostLoadReader : IHostLoadReader
{
    public HostLoad? ReadHostLoad() => null;
}
