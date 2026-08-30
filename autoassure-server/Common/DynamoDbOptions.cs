namespace A2.Server.Common;

/// <summary>DynamoDB table names used by the app's repositories.</summary>
// ReSharper disable once ClassNeverInstantiated.Global -- bound via IOptions<T> from configuration, not `new`'d directly
public record DynamoDbOptions
{
    public string RefreshTokenTableName { get; init; } = "";
    public string UserTableName { get; init; } = "";
    public string OrganizationTableName { get; init; } = "";
    public string OrganizationUserTableName { get; init; } = "";
    public string ApplicationTableName { get; init; } = "";
    public string EnvironmentTableName { get; init; } = "";
    public string EnvironmentVariableTableName { get; init; } = "";
    public string PreconditionTableName { get; init; } = "";
    public string EvidenceDefinitionTableName { get; init; } = "";
    public string ScenarioTableName { get; init; } = "";
    public string ScenariosByFolderTableName { get; init; } = "";
    public string ScenariosByTagTableName { get; init; } = "";
    public string RunTableName { get; init; } = "";
    public string TryTableName { get; init; } = "";
}
