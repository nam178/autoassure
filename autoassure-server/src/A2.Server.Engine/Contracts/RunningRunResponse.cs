namespace A2.Server.Engine.Contracts;

public record RunningRunResponse
{
    public required Guid Id { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
}
