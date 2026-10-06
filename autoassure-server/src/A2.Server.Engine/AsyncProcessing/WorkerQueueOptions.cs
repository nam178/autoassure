namespace A2.Server.Engine.AsyncProcessing;

// ReSharper disable once ClassNeverInstantiated.Global -- bound via IOptions<T> from configuration, not `new`'d directly
public record WorkerQueueOptions
{
    public string QueueUrl { get; init; } = "";
}
