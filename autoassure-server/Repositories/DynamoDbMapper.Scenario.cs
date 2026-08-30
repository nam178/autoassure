using System.Globalization;
using A2.Server.Models;
using Amazon.DynamoDBv2.Model;

namespace A2.Server.Repositories;

public static partial class DynamoDbMapper
{
    public static Dictionary<string, AttributeValue> ToDynamoDbRow(this Scenario scenario) =>
        new()
        {
            ["OrganizationId_ApplicationId"] = new(
                ApplicationScopedPartitionKey(scenario.OrganizationId, scenario.ApplicationId)
            ),
            ["Id"] = new(scenario.Id.ToString()),
            ["OrganizationId"] = new(scenario.OrganizationId.ToString()),
            ["ApplicationId"] = new(scenario.ApplicationId.ToString()),
            ["Title"] = new(scenario.Title),
            ["Description"] = new(scenario.Description),
            ["Folder"] = new(scenario.Folder),
            ["Tags"] = new AttributeValue
            {
                L = scenario.Tags.Select(tag => new AttributeValue(tag)).ToList(),
            },
            ["Activities"] = new AttributeValue
            {
                L = scenario.Activities.Select(activity => activity.ToAttributeValue()).ToList(),
            },
            ["CreatedByUserId"] = new(scenario.CreatedByUserId.ToString()),
            ["UpdatedByUserId"] = new(scenario.UpdatedByUserId.ToString()),
            ["CreatedAt"] = new(scenario.CreatedAt.ToString("O")),
            ["UpdatedAt"] = new(scenario.UpdatedAt.ToString("O")),
        };

    public static AttributeValue ToAttributeValue(this Activity activity) =>
        new()
        {
            M = new Dictionary<string, AttributeValue>
            {
                ["Id"] = new(activity.Id.ToString()),
                ["Description"] = new(activity.Description),
                ["PreconditionIds"] = new AttributeValue
                {
                    L = activity
                        .PreconditionIds.Select(id => new AttributeValue(id.ToString()))
                        .ToList(),
                },
                ["EvidenceIds"] = new AttributeValue
                {
                    L = activity
                        .EvidenceIds.Select(id => new AttributeValue(id.ToString()))
                        .ToList(),
                },
            },
        };

    public static Scenario ToScenario(this Dictionary<string, AttributeValue> row) =>
        new()
        {
            Id = Guid.Parse(row["Id"].S),
            OrganizationId = Guid.Parse(row["OrganizationId"].S),
            ApplicationId = Guid.Parse(row["ApplicationId"].S),
            Title = row["Title"].S,
            Description = row["Description"].S,
            Folder = row["Folder"].S,
            Tags = row["Tags"].L.Select(tag => tag.S).ToList(),
            Activities = row["Activities"].L.Select(item => item.ToActivity()).ToList(),
            CreatedByUserId = Guid.Parse(row["CreatedByUserId"].S),
            UpdatedByUserId = Guid.Parse(row["UpdatedByUserId"].S),
            CreatedAt = DateTimeOffset.Parse(row["CreatedAt"].S, CultureInfo.InvariantCulture),
            UpdatedAt = DateTimeOffset.Parse(row["UpdatedAt"].S, CultureInfo.InvariantCulture),
        };

    public static Activity ToActivity(this AttributeValue row) =>
        new()
        {
            Id = Guid.Parse(row.M["Id"].S),
            Description = row.M["Description"].S,
            PreconditionIds = row.M["PreconditionIds"].L.Select(id => Guid.Parse(id.S)).ToList(),
            EvidenceIds = row.M["EvidenceIds"].L.Select(id => Guid.Parse(id.S)).ToList(),
        };
}
