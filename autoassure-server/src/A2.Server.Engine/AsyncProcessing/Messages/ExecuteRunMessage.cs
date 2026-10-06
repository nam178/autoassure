namespace A2.Server.Engine.AsyncProcessing.Messages;

// ReSharper disable UnusedAutoPropertyAccessor.Global -- read by the worker handlers, which are not written yet
public sealed record ExecuteRunMessage : IWorkerMessage
{
    public static string Kind => "ExecuteRun";
    public required Guid OrganizationId { get; init; }
    public required Guid ApplicationId { get; init; }
    public required Guid RunId { get; init; }
}
