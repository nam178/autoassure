namespace A2.Server.Engine.AsyncProcessing;

public interface IWorkerQueuePublisher
{
    /// <exception cref="MessagePublishingException">
    /// The message could not be delivered to the queue service, for example because of a network error,
    /// missing permission or a rejected request. See the inner exception for details.
    /// </exception>
    Task PublishMessage<TMessage>(
        TMessage message,
        CancellationToken cancellationToken = default
    )
        where TMessage : IWorkerMessage;
}
