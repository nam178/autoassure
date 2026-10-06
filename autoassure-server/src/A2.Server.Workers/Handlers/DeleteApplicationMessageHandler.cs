using A2.Server.Engine.AsyncProcessing;
using A2.Server.Engine.AsyncProcessing.Messages;

namespace A2.Server.Workers.Handlers;

public sealed class DeleteApplicationMessageHandler
    : IWorkerMessageHandler<DeleteApplicationMessage>
{
    public Task HandleMessage(
        DeleteApplicationMessage message,
        CancellationToken cancellationToken
    ) => Task.CompletedTask;
}
