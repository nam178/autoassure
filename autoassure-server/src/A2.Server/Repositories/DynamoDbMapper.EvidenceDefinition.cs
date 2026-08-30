using System.Globalization;
using A2.Server.Models;
using Amazon.DynamoDBv2.Model;

namespace A2.Server.Repositories;

public static partial class DynamoDbMapper
{
    public static Dictionary<string, AttributeValue> ToDynamoDbRow(
        this EvidenceDefinition evidenceDefinition
    )
    {
        var row = new Dictionary<string, AttributeValue>
        {
            ["OrganizationId_ApplicationId"] = new(
                ApplicationScopedPartitionKey(
                    evidenceDefinition.OrganizationId,
                    evidenceDefinition.ApplicationId
                )
            ),
            ["Id"] = new(evidenceDefinition.Id.ToString()),
            ["OrganizationId"] = new(evidenceDefinition.OrganizationId.ToString()),
            ["ApplicationId"] = new(evidenceDefinition.ApplicationId.ToString()),
            ["Name"] = new(evidenceDefinition.Name),
            ["CreatedByUserId"] = new(evidenceDefinition.CreatedByUserId.ToString()),
            ["UpdatedByUserId"] = new(evidenceDefinition.UpdatedByUserId.ToString()),
            ["CreatedAt"] = new(evidenceDefinition.CreatedAt.ToString("O")),
            ["UpdatedAt"] = new(evidenceDefinition.UpdatedAt.ToString("O")),
            ["Description"] = new(evidenceDefinition.Description),
            ["ExampleValue"] = new(evidenceDefinition.ExampleValue),
        };

        return row;
    }

    public static EvidenceDefinition ToEvidenceDefinition(
        this Dictionary<string, AttributeValue> row
    ) =>
        new()
        {
            Id = Guid.Parse(row["Id"].S),
            OrganizationId = Guid.Parse(row["OrganizationId"].S),
            ApplicationId = Guid.Parse(row["ApplicationId"].S),
            Name = row["Name"].S,
            Description = row["Description"].S,
            ExampleValue = row["ExampleValue"].S,
            CreatedByUserId = Guid.Parse(row["CreatedByUserId"].S),
            UpdatedByUserId = Guid.Parse(row["UpdatedByUserId"].S),
            CreatedAt = DateTimeOffset.Parse(row["CreatedAt"].S, CultureInfo.InvariantCulture),
            UpdatedAt = DateTimeOffset.Parse(row["UpdatedAt"].S, CultureInfo.InvariantCulture),
        };
}
