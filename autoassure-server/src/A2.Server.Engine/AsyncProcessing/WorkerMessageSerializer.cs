using System.Text.Json;

namespace A2.Server.Engine.AsyncProcessing;

public static class WorkerMessageSerializer
{
    private static readonly JsonSerializerOptions Options = new(
        JsonSerializerDefaults.Web
    )
    {
        RespectNullableAnnotations = true,
    };

    public static string SerializeMessage<TMessage>(TMessage message)
        where TMessage : IWorkerMessage =>
        JsonSerializer.Serialize(message, Options);

    /// <exception cref="JsonException">The body is not valid JSON for
    /// <typeparamref name="TMessage"/>.</exception>
    public static TMessage DeserializeMessage<TMessage>(string messageBody)
        where TMessage : IWorkerMessage =>
        JsonSerializer.Deserialize<TMessage>(messageBody, Options)
        ?? throw new JsonException(
            $"Message body for {TMessage.Kind} is null."
        );
}
