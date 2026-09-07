namespace A2.Server.Repositories;

/// <summary>Converts domain models to/from DynamoDB rows. Shared so each repository doesn't redefine
/// the same mapping for a model it merely writes as part of another table's transaction.</summary>
public static partial class DynamoDbMapper
{
    public static string ApplicationScopedPartitionKey(Guid organizationId, Guid applicationId) =>
        $"{organizationId}_{applicationId}";

    /// <summary>The inverse of <see cref="ApplicationScopedPartitionKey"/>, for a caller that only has
    /// the combined key back from a query -- a sparse GSI's <c>KEYS_ONLY</c> projection, for
    /// instance, which returns the base table's partition key attribute as this one string rather than
    /// the OrganizationId and ApplicationId it was built from.</summary>
    public static (Guid OrganizationId, Guid ApplicationId) ParseApplicationScopedPartitionKey(
        string partitionKey
    )
    {
        var parts = partitionKey.Split('_', 2);
        return (Guid.Parse(parts[0]), Guid.Parse(parts[1]));
    }

    public static string ScenarioScopedPartitionKey(Guid organizationId, Guid scenarioId) =>
        $"{organizationId}_{scenarioId}";
}
