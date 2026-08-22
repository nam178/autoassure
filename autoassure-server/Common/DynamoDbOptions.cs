namespace A2.Server.Common;

/// <summary>DynamoDB table names used by the app's repositories.</summary>
// ReSharper disable once ClassNeverInstantiated.Global -- bound via IOptions<T> from configuration, not `new`'d directly
public record DynamoDbOptions
{
    public string RefreshTokenTableName { get; init; } = "";
}
