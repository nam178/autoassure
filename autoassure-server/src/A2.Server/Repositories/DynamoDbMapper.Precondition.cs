using System.Globalization;
using A2.Server.Models;
using Amazon.DynamoDBv2.Model;

namespace A2.Server.Repositories;

public static partial class DynamoDbMapper
{
    public static Dictionary<string, AttributeValue> ToDynamoDbRow(
        this Precondition precondition
    ) =>
        new()
        {
            ["OrganizationId_ApplicationId"] = new(
                ApplicationScopedPartitionKey(
                    precondition.OrganizationId,
                    precondition.ApplicationId
                )
            ),
            ["Id"] = new(precondition.Id.ToString()),
            ["OrganizationId"] = new(precondition.OrganizationId.ToString()),
            ["ApplicationId"] = new(precondition.ApplicationId.ToString()),
            ["Name"] = new(precondition.Name),
            ["ValueSource"] = new(precondition.ValueSource.ToString()),
            ["ExampleValue"] = new(precondition.ExampleValue),
            ["CreatedByUserId"] = new(precondition.CreatedByUserId.ToString()),
            ["UpdatedByUserId"] = new(precondition.UpdatedByUserId.ToString()),
            ["CreatedAt"] = new(precondition.CreatedAt.ToString("O")),
            ["UpdatedAt"] = new(precondition.UpdatedAt.ToString("O")),
        };

    public static Precondition ToPrecondition(this Dictionary<string, AttributeValue> row) =>
        new()
        {
            Id = Guid.Parse(row["Id"].S),
            OrganizationId = Guid.Parse(row["OrganizationId"].S),
            ApplicationId = Guid.Parse(row["ApplicationId"].S),
            Name = row["Name"].S,
            ValueSource = Enum.Parse<PreconditionValueSource>(row["ValueSource"].S),
            ExampleValue = row["ExampleValue"].S,
            CreatedByUserId = Guid.Parse(row["CreatedByUserId"].S),
            UpdatedByUserId = Guid.Parse(row["UpdatedByUserId"].S),
            CreatedAt = DateTimeOffset.Parse(row["CreatedAt"].S, CultureInfo.InvariantCulture),
            UpdatedAt = DateTimeOffset.Parse(row["UpdatedAt"].S, CultureInfo.InvariantCulture),
        };
}
