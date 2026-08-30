using System.Globalization;
using A2.Server.Models;
using Amazon.DynamoDBv2.Model;

namespace A2.Server.Repositories;

public static partial class DynamoDbMapper
{
    public static Dictionary<string, AttributeValue> ToDynamoDbRow(this Application application) =>
        new()
        {
            ["OrganizationId"] = new(application.OrganizationId.ToString()),
            ["Id"] = new(application.Id.ToString()),
            ["Name"] = new(application.Name),
            ["Description"] = new(application.Description),
            ["CreatedByUserId"] = new(application.CreatedByUserId.ToString()),
            ["UpdatedByUserId"] = new(application.UpdatedByUserId.ToString()),
            ["CreatedAt"] = new(application.CreatedAt.ToString("O")),
            ["UpdatedAt"] = new(application.UpdatedAt.ToString("O")),
        };

    public static Application ToApplication(this Dictionary<string, AttributeValue> row) =>
        new()
        {
            OrganizationId = Guid.Parse(row["OrganizationId"].S),
            Id = Guid.Parse(row["Id"].S),
            Name = row["Name"].S,
            Description = row["Description"].S,
            CreatedByUserId = Guid.Parse(row["CreatedByUserId"].S),
            UpdatedByUserId = Guid.Parse(row["UpdatedByUserId"].S),
            CreatedAt = DateTimeOffset.Parse(row["CreatedAt"].S, CultureInfo.InvariantCulture),
            UpdatedAt = DateTimeOffset.Parse(row["UpdatedAt"].S, CultureInfo.InvariantCulture),
        };
}
