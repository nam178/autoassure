using A2.Server.Engine.AsyncProcessing;
using Microsoft.Extensions.DependencyInjection;

namespace A2.Server.Workers;

public static class WorkerMessageHandlerRegistration
{
    public static IServiceCollection AddWorkerMessageHandler<
        TMessage,
        THandler
    >(this IServiceCollection services, TimeSpan timeLimit)
        where TMessage : IWorkerMessage
        where THandler : class, IWorkerMessageHandler<TMessage>
    {
        services.AddScoped<IWorkerMessageHandler<TMessage>, THandler>();
        services.AddSingleton(
            new WorkerMessageRoute
            {
                Kind = TMessage.Kind,
                TimeLimit = timeLimit,
                HandleBody = (serviceProvider, body, cancellationToken) =>
                    serviceProvider
                        .GetRequiredService<IWorkerMessageHandler<TMessage>>()
                        .HandleMessage(
                            WorkerMessageSerializer.DeserializeMessage<TMessage>(
                                body
                            ),
                            cancellationToken
                        ),
            }
        );
        return services;
    }
}
