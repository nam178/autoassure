namespace A2.Server.Contracts;

public record RunningRunResponse
{
    public required Guid Id { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
}
