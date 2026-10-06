using A2.Server.Engine.AsyncProcessing;
using A2.Server.Engine.AsyncProcessing.Messages;

namespace A2.Server.Workers.Handlers;

public sealed class DeleteScenarioMessageHandler
    : IWorkerMessageHandler<DeleteScenarioMessage>
{
    public Task HandleMessage(
        DeleteScenarioMessage message,
        CancellationToken cancellationToken
    ) => Task.CompletedTask;
}
