namespace A2.Server.Common;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
