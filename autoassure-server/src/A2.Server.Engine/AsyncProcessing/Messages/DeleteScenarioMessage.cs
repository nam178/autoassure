namespace A2.Server.Engine.AsyncProcessing.Messages;

// ReSharper disable UnusedAutoPropertyAccessor.Global -- read by the worker handlers, which are not written yet
public sealed record DeleteScenarioMessage : IWorkerMessage
{
    public static string Kind => "DeleteScenario";
    public required Guid OrganizationId { get; init; }
    public required Guid ApplicationId { get; init; }
    public required Guid ScenarioId { get; init; }
}
