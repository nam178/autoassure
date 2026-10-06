using A2.Server.Engine.AsyncProcessing;
using Amazon.Runtime;
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
        TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ReceiveFailureDelay = TimeSpan.FromSeconds(
        10
    );

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queueUrl = queueOptions.Value.QueueUrl;
        if (string.IsNullOrEmpty(queueUrl))
        {
            logger.LogCritical("WorkerQueue:QueueUrl is not configured");
            Environment.Exit(1);
        }

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
                        ProcessMessage(
                            queueUrl,
                            message,
                            freeSlots,
                            stoppingToken
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
            when (exception is AmazonServiceException or AmazonClientException)
        {
            logger.LogError(
                exception,
                "Receiving from the worker queue failed"
            );
            await Task.Delay(ReceiveFailureDelay, stoppingToken);
            return [];
        }
    }

    private async Task ProcessMessage(
        string queueUrl,
        Message message,
        SemaphoreSlim freeSlots,
        CancellationToken stoppingToken
    )
    {
        try
        {
            if (
                !message.MessageAttributes.TryGetValue(
                    KindAttributeName,
                    out var kindAttribute
                )
            )
            {
                logger.LogError(
                    "Message {MessageId} has no kind attribute; left on the queue",
                    message.MessageId
                );
                return;
            }

            using var heartbeatSource =
                CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var heartbeat = KeepMessageInvisible(
                queueUrl,
                message,
                heartbeatSource.Token
            );

            WorkerMessageDispatchResult result;
            try
            {
                result = await dispatcher.DispatchMessage(
                    kindAttribute.StringValue,
                    message.Body,
                    stoppingToken
                );
            }
            finally
            {
                await heartbeatSource.CancelAsync();
                await heartbeat;
            }

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
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Message {MessageId} interrupted by shutdown; left on the queue",
                message.MessageId
            );
        }
        catch (Exception exception)
        {
            // The message stays on the queue: it is retried, then moved to the dead-letter queue.
            logger.LogError(
                exception,
                "Message {MessageId} failed; left on the queue",
                message.MessageId
            );
        }
        finally
        {
            freeSlots.Release();
        }
    }

    private async Task KeepMessageInvisible(
        string queueUrl,
        Message message,
        CancellationToken heartbeatToken
    )
    {
        using var timer = new PeriodicTimer(VisibilityHeartbeatInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(heartbeatToken))
            {
                try
                {
                    await sqsClient.ChangeMessageVisibilityAsync(
                        new ChangeMessageVisibilityRequest
                        {
                            QueueUrl = queueUrl,
                            ReceiptHandle = message.ReceiptHandle,
                            VisibilityTimeout = VisibilityTimeoutSeconds,
                        },
                        heartbeatToken
                    );
                }
                catch (Exception exception)
                    when (exception
                            is AmazonServiceException
                                or AmazonClientException
                    )
                {
                    logger.LogWarning(
                        exception,
                        "Extending visibility of message {MessageId} failed",
                        message.MessageId
                    );
                }
            }
        }
        catch (OperationCanceledException)
        {
            // handler finished
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
            when (exception is AmazonServiceException or AmazonClientException)
        {
            // The handler is idempotent, so a second delivery is harmless.
            logger.LogWarning(
                exception,
                "Deleting handled message {MessageId} failed",
                message.MessageId
            );
        }
    }
}
