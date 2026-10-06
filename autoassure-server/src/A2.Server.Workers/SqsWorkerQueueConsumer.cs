using System.Text.Json;
using A2.Server.Engine.AsyncProcessing;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace A2.Server.Workers;

public sealed class SqsWorkerQueueConsumer(
    IAmazonSQS sqsClient,
    IOptions<WorkerQueueOptions> queueOptions,
    WorkerMessageDispatcher dispatcher,
    ILogger<SqsWorkerQueueConsumer> logger
) : BackgroundService
{
    private const string KindAttributeName = "kind";
    private const int MaxConcurrentMessages = 4;

    // Must match visibility_timeout_seconds of the worker queue in autoassure-infra/sqs.tf.
    private const int VisibilityTimeoutSeconds = 900;

    private static readonly TimeSpan VisibilityHeartbeatInterval =
        TimeSpan.FromMinutes(1);

    private static readonly TimeSpan ReceiveFailureDelay = TimeSpan.FromSeconds(
        10
    );

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queueUrl = queueOptions.Value.QueueUrl;
        logger.LogInformation("Consuming worker queue {QueueUrl}", queueUrl);
        using var freeSlots = new SemaphoreSlim(
            MaxConcurrentMessages,
            MaxConcurrentMessages
        );
        var runningMessages = new List<Task>();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await freeSlots.WaitAsync(stoppingToken);
                freeSlots.Release();
                var receivedMessages = await ReceiveMessages(
                    queueUrl,
                    freeSlots.CurrentCount,
                    stoppingToken
                );

                foreach (var message in receivedMessages)
                {
                    await freeSlots.WaitAsync(stoppingToken);
                    runningMessages.Add(
                        RunThenRelease(
                            HandleMessage(queueUrl, message, stoppingToken),
                            message.MessageId,
                            freeSlots
                        )
                    );
                }

                runningMessages.RemoveAll(task => task.IsCompleted);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // host is shutting down
        }

        await Task.WhenAll(runningMessages);
        logger.LogInformation("Worker queue consumer stopped");
    }

    private async Task<IReadOnlyList<Message>> ReceiveMessages(
        string queueUrl,
        int maxMessages,
        CancellationToken stoppingToken
    )
    {
        try
        {
            var response = await sqsClient.ReceiveMessageAsync(
                new ReceiveMessageRequest
                {
                    QueueUrl = queueUrl,
                    MaxNumberOfMessages = Math.Min(maxMessages, 10),
                    MessageAttributeNames = [KindAttributeName],
                    WaitTimeSeconds = 20,
                },
                stoppingToken
            );
            return response.Messages ?? [];
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Receiving from the worker queue failed"
            );
            await Task.Delay(ReceiveFailureDelay, stoppingToken);
            return [];
        }
    }

    private async Task RunThenRelease(
        Task work,
        string messageId,
        SemaphoreSlim semaphore
    )
    {
        try
        {
            await work;
        }
        catch (Exception exception)
        {
            // The message stays on the queue: it is retried, then moved to the dead-letter queue.
            logger.LogError(
                exception,
                "Message {MessageId} failed; left on the queue",
                messageId
            );
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <exception cref="Exception">
    /// Any exception the handler throws is rethrown unchanged.
    /// </exception>
    private async Task HandleMessage(
        string queueUrl,
        Message message,
        CancellationToken programCancellationToken
    )
    {
        // Read message "kind", so it can be deserialized to the right type later.
        if (
            !message.MessageAttributes.TryGetValue(
                KindAttributeName,
                out var kindAttribute
            )
        )
        {
            logger.LogError(
                "Message {MessageId} has no kind attribute; returned to the queue now so it reaches the dead-letter queue sooner",
                message.MessageId
            );
            await ReturnMessageToQueue(queueUrl, message);
            return;
        }

        // Dispatch to the dispatcher for processing
        WorkerMessageDispatchResult result;
        try
        {
            result = await DispatchWithHeartbeat(
                queueUrl,
                message,
                kindAttribute.StringValue,
                programCancellationToken
            );
        }
        catch (OperationCanceledException)
            when (programCancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Message {MessageId} interrupted by shutdown; returned to the queue",
                message.MessageId
            );
            await ReturnMessageToQueue(queueUrl, message);
            return;
        }
        catch (JsonException exception)
        {
            logger.LogError(
                exception,
                "Message {MessageId} has a body that does not match kind {Kind}; returned to the queue now so it reaches the dead-letter queue sooner",
                message.MessageId,
                kindAttribute.StringValue
            );
            await ReturnMessageToQueue(queueUrl, message);
            return;
        }

        // When processing completes, delete the message.
        switch (result)
        {
            case WorkerMessageDispatchResult.Handled:
                await DeleteMessage(queueUrl, message);
                logger.LogInformation(
                    "Handled {Kind} message {MessageId}",
                    kindAttribute.StringValue,
                    message.MessageId
                );
                break;
            case WorkerMessageDispatchResult.UnknownKind:
                logger.LogError(
                    "Message {MessageId} has unknown kind {Kind}; left on the queue",
                    message.MessageId,
                    kindAttribute.StringValue
                );
                break;
            case WorkerMessageDispatchResult.TimeLimitExceeded:
                logger.LogError(
                    "Message {MessageId} of kind {Kind} hit its time limit; left on the queue",
                    message.MessageId,
                    kindAttribute.StringValue
                );
                break;
        }
    }

    /// <summary>
    /// Dispatches one message and keeps it invisible on the queue until the
    /// handler finishes.
    /// </summary>
    /// <exception cref="OperationCanceledException">
    /// When <paramref name="programCancellationToken" /> is cancelled, which
    /// means the host is shutting down.
    /// </exception>
    /// <exception cref="System.Text.Json.JsonException">
    /// When the body of <paramref name="message" /> does not match its kind.
    /// </exception>
    /// <exception cref="Exception">
    /// Any exception the handler throws is rethrown unchanged.
    /// </exception>
    private async Task<WorkerMessageDispatchResult> DispatchWithHeartbeat(
        string queueUrl,
        Message message,
        string kind,
        CancellationToken programCancellationToken
    )
    {
        // Start a heartbeat loop
        using var heartbeatCancellationToken =
            CancellationTokenSource.CreateLinkedTokenSource(
                programCancellationToken
            );
        var heartbeat = SendHeartBeats(
            queueUrl,
            message,
            heartbeatCancellationToken.Token
        );

        // Pass the message to the dispatcher for processing
        try
        {
            return await dispatcher.DispatchMessage(
                kind,
                message.Body,
                programCancellationToken
            );
        }
        finally
        {
            // Once finish, stop the heartbeat loop
            await heartbeatCancellationToken.CancelAsync();
            await heartbeat;
        }
    }

    private async Task SendHeartBeats(
        string queueUrl,
        Message message,
        CancellationToken cancellationToken
    )
    {
        using var timer = new PeriodicTimer(VisibilityHeartbeatInterval);
        while (true)
        {
            try
            {
                await timer.WaitForNextTickAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await sqsClient.ChangeMessageVisibilityAsync(
                    new ChangeMessageVisibilityRequest
                    {
                        QueueUrl = queueUrl,
                        ReceiptHandle = message.ReceiptHandle,
                        VisibilityTimeout = VisibilityTimeoutSeconds,
                    },
                    cancellationToken
                );
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Extending visibility of message {MessageId} failed; will try again",
                    message.MessageId
                );
            }
        }
    }

    private async Task ReturnMessageToQueue(string queueUrl, Message message)
    {
        try
        {
            await sqsClient.ChangeMessageVisibilityAsync(
                new ChangeMessageVisibilityRequest
                {
                    QueueUrl = queueUrl,
                    ReceiptHandle = message.ReceiptHandle,
                    VisibilityTimeout = 0,
                }
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Returning message {MessageId} to the queue failed",
                message.MessageId
            );
        }
    }

    private async Task DeleteMessage(string queueUrl, Message message)
    {
        try
        {
            await sqsClient.DeleteMessageAsync(
                new DeleteMessageRequest
                {
                    QueueUrl = queueUrl,
                    ReceiptHandle = message.ReceiptHandle,
                }
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Deleting handled message {MessageId} failed",
                message.MessageId
            );
        }
    }
}
