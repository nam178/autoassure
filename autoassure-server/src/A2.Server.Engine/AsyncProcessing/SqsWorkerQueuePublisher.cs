using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.Engine.AsyncProcessing;

public sealed class SqsWorkerQueuePublisher(
    IAmazonSQS sqsClient,
    IOptions<WorkerQueueOptions> workerQueueOptions
) : IWorkerQueuePublisher
{
    public const string KindAttributeName = "kind";
    public const string OrganizationIdAttributeName = "organizationId";

    /// <exception cref="MessagePublishingException">
    /// The message could not be delivered to the queue service, for example because of a network error,
    /// missing permission or a rejected request. See the inner exception for details.
    /// </exception>
    public async Task PublishMessage<TMessage>(
        TMessage message,
        CancellationToken cancellationToken = default
    )
        where TMessage : IWorkerMessage
    {
        var sendMessageRequest = new SendMessageRequest
        {
            QueueUrl = workerQueueOptions.Value.QueueUrl,
            MessageBody = WorkerMessageSerializer.SerializeMessage(message),
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                [KindAttributeName] = ToStringAttribute(TMessage.Kind),
                [OrganizationIdAttributeName] = ToStringAttribute(
                    message.OrganizationId.ToString()
                ),
            },
        };

        try
        {
            await sqsClient.SendMessageAsync(
                sendMessageRequest,
                cancellationToken
            );
        }
        catch (Exception exception)
            when (exception is AmazonServiceException or AmazonClientException)
        {
            throw new MessagePublishingException(
                $"Could not publish {TMessage.Kind} message to the worker queue: {exception.Message}",
                exception
            );
        }
    }

    private static MessageAttributeValue ToStringAttribute(
        string attributeValue
    ) => new() { DataType = "String", StringValue = attributeValue };
}
