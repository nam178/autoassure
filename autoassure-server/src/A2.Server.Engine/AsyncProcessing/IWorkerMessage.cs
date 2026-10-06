namespace A2.Server.Engine.AsyncProcessing;

public interface IWorkerMessage
{
    static abstract string Kind { get; }
    Guid OrganizationId { get; }
}
