namespace A2.Server.Workers;

// ReSharper disable once ClassNeverInstantiated.Global -- bound via IOptions<T> from configuration, not `new`'d directly
public record WorkerVisibilityOptions
{
    public int VisibilityTimeoutSeconds { get; init; }
}
