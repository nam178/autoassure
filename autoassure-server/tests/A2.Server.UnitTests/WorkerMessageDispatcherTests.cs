using A2.Server.Engine.AsyncProcessing;
using A2.Server.Engine.AsyncProcessing.Messages;
using A2.Server.Workers;
using Microsoft.Extensions.DependencyInjection;

namespace A2.Server.UnitTests;

public class WorkerMessageDispatcherTests
{
    [Fact]
    public async Task DispatchMessage_WhenKindIsRegistered_HandlesDeserializedMessage()
    {
        // setup
        var handler = new RecordingHandler();
        var dispatcher = CreateDispatcher(handler, TimeSpan.FromMinutes(1));
        var message = new DeleteScenarioMessage
        {
            OrganizationId = Guid.CreateVersion7(),
            ApplicationId = Guid.CreateVersion7(),
            ScenarioId = Guid.CreateVersion7(),
        };

        // test
        var result = await dispatcher.DispatchMessage(
            DeleteScenarioMessage.Kind,
            WorkerMessageSerializer.SerializeMessage(message),
            CancellationToken.None
        );

        // verify
        Assert.Equal(WorkerMessageDispatchResult.Handled, result);
        Assert.Equal(message, Assert.Single(handler.HandledMessages));
    }

    [Fact]
    public async Task DispatchMessage_WhenKindIsUnknown_ReturnsUnknownKindWithoutHandling()
    {
        // setup
        var handler = new RecordingHandler();
        var dispatcher = CreateDispatcher(handler, TimeSpan.FromMinutes(1));

        // test
        var result = await dispatcher.DispatchMessage(
            "NoSuchKind",
            "{}",
            CancellationToken.None
        );

        // verify
        Assert.Equal(WorkerMessageDispatchResult.UnknownKind, result);
        Assert.Empty(handler.HandledMessages);
    }

    [Fact]
    public async Task DispatchMessage_WhenHandlerThrows_PropagatesTheException()
    {
        // setup
        var handler = new RecordingHandler
        {
            Failure = new InvalidOperationException("boom"),
        };
        var dispatcher = CreateDispatcher(handler, TimeSpan.FromMinutes(1));

        // test + verify
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.DispatchMessage(
                DeleteScenarioMessage.Kind,
                ValidBody(),
                CancellationToken.None
            )
        );
        Assert.Equal("boom", thrown.Message);
    }

    [Fact]
    public async Task DispatchMessage_WhenBodyDoesNotMatchKind_ThrowsJsonException()
    {
        // setup
        var dispatcher = CreateDispatcher(
            new RecordingHandler(),
            TimeSpan.FromMinutes(1)
        );

        // test + verify
        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() =>
            dispatcher.DispatchMessage(
                DeleteScenarioMessage.Kind,
                "{\"organizationId\":\"" + Guid.CreateVersion7() + "\"}",
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task DispatchMessage_WhenHandlerRunsPastTimeLimit_ReturnsTimeLimitExceeded()
    {
        // setup
        var handler = new RecordingHandler { WaitForCancellation = true };
        var dispatcher = CreateDispatcher(
            handler,
            TimeSpan.FromMilliseconds(50)
        );

        // test
        var result = await dispatcher.DispatchMessage(
            DeleteScenarioMessage.Kind,
            ValidBody(),
            CancellationToken.None
        );

        // verify
        Assert.Equal(WorkerMessageDispatchResult.TimeLimitExceeded, result);
    }

    [Fact]
    public async Task DispatchMessage_WhenShutdownIsRequested_ThrowsOperationCanceled()
    {
        // setup
        var handler = new RecordingHandler { WaitForCancellation = true };
        var dispatcher = CreateDispatcher(handler, TimeSpan.FromMinutes(1));
        using var shutdown = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(50)
        );

        // test + verify
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            dispatcher.DispatchMessage(
                DeleteScenarioMessage.Kind,
                ValidBody(),
                shutdown.Token
            )
        );
    }

    private static string ValidBody() =>
        WorkerMessageSerializer.SerializeMessage(
            new DeleteScenarioMessage
            {
                OrganizationId = Guid.CreateVersion7(),
                ApplicationId = Guid.CreateVersion7(),
                ScenarioId = Guid.CreateVersion7(),
            }
        );

    private static WorkerMessageDispatcher CreateDispatcher(
        RecordingHandler handler,
        TimeSpan timeLimit
    )
    {
        var services = new ServiceCollection();
        services.AddWorkerMessageHandler<
            DeleteScenarioMessage,
            RecordingHandler
        >(timeLimit);
        services.AddScoped(_ => handler);
        services.AddScoped<IWorkerMessageHandler<DeleteScenarioMessage>>(
            provider => provider.GetRequiredService<RecordingHandler>()
        );
        var provider = services.BuildServiceProvider();
        return new WorkerMessageDispatcher(
            provider.GetServices<WorkerMessageRoute>(),
            provider.GetRequiredService<IServiceScopeFactory>()
        );
    }

    private sealed class RecordingHandler
        : IWorkerMessageHandler<DeleteScenarioMessage>
    {
        public List<DeleteScenarioMessage> HandledMessages { get; } = [];
        public Exception? Failure { get; init; }
        public bool WaitForCancellation { get; init; }

        public async Task HandleMessage(
            DeleteScenarioMessage message,
            CancellationToken cancellationToken
        )
        {
            if (Failure is not null)
                throw Failure;
            if (WaitForCancellation)
                await Task.Delay(Timeout.Infinite, cancellationToken);
            HandledMessages.Add(message);
        }
    }
}
