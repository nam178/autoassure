using System.Reflection;
using A2.Server.Engine.AsyncProcessing;
using A2.Server.Engine.AsyncProcessing.Messages;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.UnitTests;

public sealed class SqsWorkerQueuePublisherTests
{
    private const string QueueUrl = "https://sqs.test/worker-queue";

    [Fact]
    public async Task PublishMessage_WhenExecuteRunMessage_SendsBodyAndKindAttribute()
    {
        // setup
        var sqsClient = SendRecordingSqsClient.Create(out var sentRequests);
        var publisher = new SqsWorkerQueuePublisher(
            sqsClient,
            Options.Create(new WorkerQueueOptions { QueueUrl = QueueUrl })
        );
        var message = new ExecuteRunMessage
        {
            OrganizationId = Guid.CreateVersion7(),
            ApplicationId = Guid.CreateVersion7(),
            RunId = Guid.CreateVersion7(),
        };

        // test
        await publisher.PublishMessage(message);

        // verify
        var sentRequest = Assert.Single(sentRequests);
        Assert.Equal(QueueUrl, sentRequest.QueueUrl);
        Assert.Equal(
            "ExecuteRun",
            sentRequest
                .MessageAttributes[SqsWorkerQueuePublisher.KindAttributeName]
                .StringValue
        );
        Assert.Equal(
            message.OrganizationId.ToString(),
            sentRequest
                .MessageAttributes[
                    SqsWorkerQueuePublisher.OrganizationIdAttributeName
                ]
                .StringValue
        );
        Assert.Equal(
            message,
            WorkerMessageSerializer.DeserializeMessage<ExecuteRunMessage>(
                sentRequest.MessageBody
            )
        );
    }

    [Fact]
    public async Task PublishMessage_WhenDeleteScenarioMessage_SendsDeleteScenarioKind()
    {
        // setup
        var sqsClient = SendRecordingSqsClient.Create(out var sentRequests);
        var publisher = new SqsWorkerQueuePublisher(
            sqsClient,
            Options.Create(new WorkerQueueOptions { QueueUrl = QueueUrl })
        );

        // test
        await publisher.PublishMessage(
            new DeleteScenarioMessage
            {
                OrganizationId = Guid.CreateVersion7(),
                ApplicationId = Guid.CreateVersion7(),
                ScenarioId = Guid.CreateVersion7(),
            }
        );

        // verify
        var sentRequest = Assert.Single(sentRequests);
        Assert.Equal(
            "DeleteScenario",
            sentRequest
                .MessageAttributes[SqsWorkerQueuePublisher.KindAttributeName]
                .StringValue
        );
    }

    [Theory]
    [MemberData(nameof(SqsFailures))]
    public async Task PublishMessage_WhenSqsFails_ThrowsMessagePublishingException(
        Exception sqsFailure
    )
    {
        // setup
        var sqsClient = SendRecordingSqsClient.Create(out _, sqsFailure);
        var publisher = new SqsWorkerQueuePublisher(
            sqsClient,
            Options.Create(new WorkerQueueOptions { QueueUrl = QueueUrl })
        );

        // test
        var exception = await Assert.ThrowsAsync<MessagePublishingException>(
            () =>
                publisher.PublishMessage(
                    new ExecuteRunMessage
                    {
                        OrganizationId = Guid.CreateVersion7(),
                        ApplicationId = Guid.CreateVersion7(),
                        RunId = Guid.CreateVersion7(),
                    }
                )
        );

        // verify
        Assert.Same(sqsFailure, exception.InnerException);
        Assert.Contains("ExecuteRun", exception.Message);
        Assert.Contains(sqsFailure.Message, exception.Message);
    }

    [Fact]
    public async Task PublishMessage_WhenCancelled_DoesNotWrapOperationCanceledException()
    {
        // setup
        var sqsClient = SendRecordingSqsClient.Create(
            out _,
            new OperationCanceledException()
        );
        var publisher = new SqsWorkerQueuePublisher(
            sqsClient,
            Options.Create(new WorkerQueueOptions { QueueUrl = QueueUrl })
        );

        // test + verify
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            publisher.PublishMessage(
                new DeleteApplicationMessage
                {
                    OrganizationId = Guid.CreateVersion7(),
                    ApplicationId = Guid.CreateVersion7(),
                }
            )
        );
    }

    public static TheoryData<Exception> SqsFailures() =>
        [
            new AmazonSQSException("Access denied"),
            new AmazonClientException("Network is unreachable"),
        ];

    // Hand-written fake: records SendMessageAsync(SendMessageRequest) calls, everything else is unsupported.
    private class SendRecordingSqsClient : DispatchProxy
    {
        private readonly List<SendMessageRequest> _sentRequests = [];
        private Exception? _sendFailure;

        public static IAmazonSQS Create(
            out List<SendMessageRequest> sentRequests,
            Exception? sendFailure = null
        )
        {
            var fake = Create<IAmazonSQS, SendRecordingSqsClient>();
            // ReSharper disable once SuspiciousTypeConversion.Global -- DispatchProxy makes the fake an instance of this class
            // ReSharper disable once RedundantCast
            var recorder = (SendRecordingSqsClient)(object)fake;
            recorder._sendFailure = sendFailure;
            sentRequests = recorder._sentRequests;
            return fake;
        }

        protected override object Invoke(
            MethodInfo? targetMethod,
            object?[]? args
        )
        {
            if (
                targetMethod?.Name == nameof(IAmazonSQS.SendMessageAsync)
                && args?[0] is SendMessageRequest request
            )
            {
                _sentRequests.Add(request);
                if (_sendFailure is not null)
                    return Task.FromException<SendMessageResponse>(
                        _sendFailure
                    );
                return Task.FromResult(new SendMessageResponse());
            }

            throw new NotSupportedException(
                $"Unexpected call: {targetMethod?.Name}"
            );
        }
    }
}
