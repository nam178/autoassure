namespace A2.Server.Engine.AsyncProcessing;

/// <summary>
/// Handles one kind of worker queue message.
/// <list type="bullet">
/// <item>Delivery is at-least-once, so the same message can arrive twice, even
/// at the same time on two workers. Make every handler safe to run
/// twice.</item>
/// <item>Stop when the cancellation token is cancelled. It fires when the time
/// limit for this message kind is reached, or when the worker shuts down. The
/// worker cannot kill a running handler.</item>
/// <item>Return normally to finish the message and delete it from the queue.
/// Throw to leave it on the queue, where it is retried and finally moved to the
/// dead-letter queue.</item>
/// </list>
/// </summary>
public interface IWorkerMessageHandler<in TMessage>
    where TMessage : IWorkerMessage
{
    Task HandleMessage(TMessage message, CancellationToken cancellationToken);
}
