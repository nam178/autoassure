namespace A2.Server.Workers;

public sealed record WorkerMessageRoute
{
    public required string Kind { get; init; }
    public required TimeSpan TimeLimit { get; init; }
    public required Func<
        IServiceProvider,
        string,
        CancellationToken,
        Task
    > HandleBody { get; init; }
}
