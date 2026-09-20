using System.Globalization;
using A2.Server.Models;
using Amazon.DynamoDBv2.Model;

namespace A2.Server.Repositories;

public static partial class DynamoDbMapper
{
    public static Dictionary<string, AttributeValue> ToDynamoDbRow(
        this Scenario scenario
    )
    {
        return new Dictionary<string, AttributeValue>
        {
            ["OrganizationId_ApplicationId"] = new(
                ApplicationScopedPartitionKey(
                    scenario.OrganizationId,
                    scenario.ApplicationId
                )
            ),
            ["Id"] = new(scenario.Id.ToString()),
            ["OrganizationId"] = new(scenario.OrganizationId.ToString()),
            ["ApplicationId"] = new(scenario.ApplicationId.ToString()),
            ["Title"] = new(scenario.Title),
            ["Description"] = new(scenario.Description),
            ["Folder"] = new(scenario.Folder),
            ["Tags"] = new()
            {
                L = scenario
                    .Tags.Select(tag => new AttributeValue(tag))
                    .ToList(),
            },
            ["ActivityCount"] = new()
            {
                N = scenario.ActivityCount.ToString(
                    CultureInfo.InvariantCulture
                ),
            },
            ["CreatedByUserId"] = new(scenario.CreatedByUserId.ToString()),
            ["UpdatedByUserId"] = new(scenario.UpdatedByUserId.ToString()),
            ["CreatedAt"] = new(scenario.CreatedAt.ToString("O")),
            ["UpdatedAt"] = new(scenario.UpdatedAt.ToString("O")),
        };
    }

    public static Scenario ToScenario(
        this Dictionary<string, AttributeValue> row
    )
    {
        return new Scenario
        {
            Id = Guid.Parse(row["Id"].S),
            OrganizationId = Guid.Parse(row["OrganizationId"].S),
            ApplicationId = Guid.Parse(row["ApplicationId"].S),
            Title = row["Title"].S,
            Description = row["Description"].S,
            Folder = row["Folder"].S,
            Tags = row["Tags"].L.Select(tag => tag.S).ToList(),
            // Rows written before ActivityCount existed don't have it -- Then default to 0, since
            // Scenarios created before that point have no way to have accrued a count anyway.
            ActivityCount = row.TryGetValue(
                "ActivityCount",
                out var activityCount
            )
                ? int.Parse(activityCount.N, CultureInfo.InvariantCulture)
                : 0,
            CreatedByUserId = Guid.Parse(row["CreatedByUserId"].S),
            UpdatedByUserId = Guid.Parse(row["UpdatedByUserId"].S),
            CreatedAt = DateTimeOffset.Parse(
                row["CreatedAt"].S,
                CultureInfo.InvariantCulture
            ),
            UpdatedAt = DateTimeOffset.Parse(
                row["UpdatedAt"].S,
                CultureInfo.InvariantCulture
            ),
        };
    }
}