using System.Globalization;
using A2.Server.Models;
using Amazon.DynamoDBv2.Model;

namespace A2.Server.Repositories;

public static partial class DynamoDbMapper
{
    public static Dictionary<string, AttributeValue> ToDynamoDbRow(this Activity activity) =>
        new()
        {
            ["OrganizationId_ScenarioId"] = new(
                ScenarioScopedPartitionKey(activity.OrganizationId, activity.ScenarioId)
            ),
            ["Id"] = new(activity.Id.ToString()),
            ["OrganizationId"] = new(activity.OrganizationId.ToString()),
            ["ApplicationId"] = new(activity.ApplicationId.ToString()),
            ["ScenarioId"] = new(activity.ScenarioId.ToString()),
            ["Description"] = new(activity.Description),
            ["Order"] = new AttributeValue
            {
                N = activity.Order.ToString(CultureInfo.InvariantCulture),
            },
            ["PreconditionIds"] = new AttributeValue
            {
                L = activity
                    .PreconditionIds.Select(id => new AttributeValue(id.ToString()))
                    .ToList(),
            },
            ["EvidenceIds"] = new AttributeValue
            {
                L = activity.EvidenceIds.Select(id => new AttributeValue(id.ToString())).ToList(),
            },
            ["CreatedByUserId"] = new(activity.CreatedByUserId.ToString()),
            ["UpdatedByUserId"] = new(activity.UpdatedByUserId.ToString()),
            ["CreatedAt"] = new(activity.CreatedAt.ToString("O")),
            ["UpdatedAt"] = new(activity.UpdatedAt.ToString("O")),
        };

    public static Activity ToActivity(this Dictionary<string, AttributeValue> row) =>
        new()
        {
            Id = Guid.Parse(row["Id"].S),
            OrganizationId = Guid.Parse(row["OrganizationId"].S),
            ApplicationId = Guid.Parse(row["ApplicationId"].S),
            ScenarioId = Guid.Parse(row["ScenarioId"].S),
            Description = row["Description"].S,
            Order = int.Parse(row["Order"].N, CultureInfo.InvariantCulture),
            PreconditionIds = row["PreconditionIds"].L.Select(id => Guid.Parse(id.S)).ToList(),
            EvidenceIds = row["EvidenceIds"].L.Select(id => Guid.Parse(id.S)).ToList(),
            CreatedByUserId = Guid.Parse(row["CreatedByUserId"].S),
            UpdatedByUserId = Guid.Parse(row["UpdatedByUserId"].S),
            CreatedAt = DateTimeOffset.Parse(row["CreatedAt"].S, CultureInfo.InvariantCulture),
            UpdatedAt = DateTimeOffset.Parse(row["UpdatedAt"].S, CultureInfo.InvariantCulture),
        };
}
