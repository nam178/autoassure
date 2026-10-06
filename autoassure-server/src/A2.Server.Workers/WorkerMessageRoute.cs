namespace A2.Server.Workers;

public sealed record WorkerMessageRoute
{
    public required string Kind { get; init; }
    public required TimeSpan TimeLimit { get; init; }

    /// <summary>
    /// Deserializes the body and runs the handler.
    /// </summary>
    /// <exception cref="System.Text.Json.JsonException">
    /// When the body does not match the message type of this kind.
    /// </exception>
    /// <exception cref="Exception">
    /// Any exception the handler throws is rethrown unchanged.
    /// </exception>
    public required Func<
        IServiceProvider,
        string,
        CancellationToken,
        Task
    > HandleBody { get; init; }
}
