using A2.Server.Engine.AsyncProcessing;
using A2.Server.Engine.AsyncProcessing.Messages;

namespace A2.Server.Workers.Handlers;

public sealed class ExecuteRunMessageHandler
    : IWorkerMessageHandler<ExecuteRunMessage>
{
    public Task HandleMessage(
        ExecuteRunMessage message,
        CancellationToken cancellationToken
    ) => Task.CompletedTask;
}
