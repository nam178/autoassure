namespace A2.Server.Repositories;

/// <summary>Converts domain models to/from DynamoDB rows. Shared so each repository doesn't redefine
/// the same mapping for a model it merely writes as part of another table's transaction.</summary>
public static partial class DynamoDbMapper
{
    public static string ApplicationScopedPartitionKey(Guid organizationId, Guid applicationId) =>
        $"{organizationId}_{applicationId}";
}
