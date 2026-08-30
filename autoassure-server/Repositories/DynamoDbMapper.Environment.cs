using System.Globalization;
using A2.Server.Models;
using Amazon.DynamoDBv2.Model;
using Environment = A2.Server.Models.Environment;

namespace A2.Server.Repositories;

public static partial class DynamoDbMapper
{
    public static Dictionary<string, AttributeValue> ToDynamoDbRow(this Environment environment) =>
        new()
        {
            ["OrganizationId_ApplicationId"] = new(
                ApplicationScopedPartitionKey(environment.OrganizationId, environment.ApplicationId)
            ),
            ["Id"] = new(environment.Id.ToString()),
            ["OrganizationId"] = new(environment.OrganizationId.ToString()),
            ["ApplicationId"] = new(environment.ApplicationId.ToString()),
            ["Name"] = new(environment.Name),
            ["Classification"] = new(environment.Classification.ToString()),
            ["CreatedByUserId"] = new(environment.CreatedByUserId.ToString()),
            ["UpdatedByUserId"] = new(environment.UpdatedByUserId.ToString()),
            ["CreatedAt"] = new(environment.CreatedAt.ToString("O")),
            ["UpdatedAt"] = new(environment.UpdatedAt.ToString("O")),
        };

    public static Environment ToEnvironment(this Dictionary<string, AttributeValue> row) =>
        new()
        {
            Id = Guid.Parse(row["Id"].S),
            OrganizationId = Guid.Parse(row["OrganizationId"].S),
            ApplicationId = Guid.Parse(row["ApplicationId"].S),
            Name = row["Name"].S,
            Classification = Enum.Parse<EnvironmentClassification>(row["Classification"].S),
            CreatedByUserId = Guid.Parse(row["CreatedByUserId"].S),
            UpdatedByUserId = Guid.Parse(row["UpdatedByUserId"].S),
            CreatedAt = DateTimeOffset.Parse(row["CreatedAt"].S, CultureInfo.InvariantCulture),
            UpdatedAt = DateTimeOffset.Parse(row["UpdatedAt"].S, CultureInfo.InvariantCulture),
        };

    public static EnvironmentVariable ToEnvironmentVariable(
        this Dictionary<string, AttributeValue> row
    ) =>
        new()
        {
            OrganizationId = Guid.Parse(row["OrganizationId"].S),
            EnvironmentId = Guid.Parse(row["EnvironmentId"].S),
            Key = row["Key"].S,
            Value = row["Value"].S,
            CreatedByUserId = Guid.Parse(row["CreatedByUserId"].S),
            UpdatedByUserId = Guid.Parse(row["UpdatedByUserId"].S),
            CreatedAt = DateTimeOffset.Parse(row["CreatedAt"].S, CultureInfo.InvariantCulture),
            UpdatedAt = DateTimeOffset.Parse(row["UpdatedAt"].S, CultureInfo.InvariantCulture),
        };
}
