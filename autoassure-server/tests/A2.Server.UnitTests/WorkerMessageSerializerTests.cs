using System.Text.Json;
using A2.Server.Engine.AsyncProcessing;
using A2.Server.Engine.AsyncProcessing.Messages;

namespace A2.Server.UnitTests;

public sealed class WorkerMessageSerializerTests
{
    [Fact]
    public void DeserializeMessage_WhenDeleteScenarioMessageRoundTrips_ReturnsSameMessage()
    {
        // setup
        var message = new DeleteScenarioMessage
        {
            OrganizationId = Guid.CreateVersion7(),
            ApplicationId = Guid.CreateVersion7(),
            ScenarioId = Guid.CreateVersion7(),
        };

        // test
        var roundTripped =
            WorkerMessageSerializer.DeserializeMessage<DeleteScenarioMessage>(
                WorkerMessageSerializer.SerializeMessage(message)
            );

        // verify
        Assert.Equal(message, roundTripped);
    }

    [Fact]
    public void DeserializeMessage_WhenDeleteApplicationMessageRoundTrips_ReturnsSameMessage()
    {
        // setup
        var message = new DeleteApplicationMessage
        {
            OrganizationId = Guid.CreateVersion7(),
            ApplicationId = Guid.CreateVersion7(),
        };

        // test
        var roundTripped =
            WorkerMessageSerializer.DeserializeMessage<DeleteApplicationMessage>(
                WorkerMessageSerializer.SerializeMessage(message)
            );

        // verify
        Assert.Equal(message, roundTripped);
    }

    [Fact]
    public void DeserializeMessage_WhenExecuteRunMessageRoundTrips_ReturnsSameMessage()
    {
        // setup
        var message = new ExecuteRunMessage
        {
            OrganizationId = Guid.CreateVersion7(),
            ApplicationId = Guid.CreateVersion7(),
            RunId = Guid.CreateVersion7(),
        };

        // test
        var roundTripped =
            WorkerMessageSerializer.DeserializeMessage<ExecuteRunMessage>(
                WorkerMessageSerializer.SerializeMessage(message)
            );

        // verify
        Assert.Equal(message, roundTripped);
    }

    [Fact]
    public void DeserializeMessage_WhenRequiredFieldIsMissing_ThrowsJsonException()
    {
        // setup
        var messageBody =
            $$"""{"organizationId":"{{Guid.CreateVersion7()}}"}""";

        // test + verify
        Assert.Throws<JsonException>(() =>
            WorkerMessageSerializer.DeserializeMessage<ExecuteRunMessage>(
                messageBody
            )
        );
    }

    [Fact]
    public void DeserializeMessage_WhenBodyIsJsonNull_ThrowsJsonException()
    {
        // test + verify
        Assert.Throws<JsonException>(() =>
            WorkerMessageSerializer.DeserializeMessage<ExecuteRunMessage>(
                "null"
            )
        );
    }
}
