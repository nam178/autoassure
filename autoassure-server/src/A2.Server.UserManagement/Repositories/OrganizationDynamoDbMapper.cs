using System.Globalization;
using A2.Server.Common;
using A2.Server.UserManagement.Models;
using Amazon.DynamoDBv2.Model;

namespace A2.Server.UserManagement.Repositories;

public static class OrganizationDynamoDbMapper
{
    public static Dictionary<string, AttributeValue> ToDynamoDbRow(
        this Organization organization
    )
    {
        return new Dictionary<string, AttributeValue>
        {
            ["Id"] = new(organization.Id.ToString()),
            ["Name"] = new(organization.Name),
            ["IsPersonal"] = new() { BOOL = organization.IsPersonal },
            ["CreatedByUserId"] = new(organization.CreatedByUserId.ToString()),
            ["UpdatedByUserId"] = new(organization.UpdatedByUserId.ToString()),
            ["CreatedAt"] = new(organization.CreatedAt.ToString("O")),
            ["UpdatedAt"] = new(organization.UpdatedAt.ToString("O")),
            ["LifecycleState"] = new(organization.LifecycleState.ToString()),
            ["OwnerCount"] = new()
            {
                N = organization.OwnerCount.ToString(
                    CultureInfo.InvariantCulture
                ),
            },
        };
    }

    public static Organization ToOrganization(
        this Dictionary<string, AttributeValue> row
    )
    {
        return new Organization
        {
            Id = Guid.Parse(row["Id"].S),
            Name = row["Name"].S,
            IsPersonal = row["IsPersonal"].RequireBool("IsPersonal"),
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
            LifecycleState = row.TryGetValue(
                "LifecycleState",
                out var lifecycleState
            )
                ? Enum.Parse<LifecycleState>(lifecycleState.S)
                : LifecycleState.Active,
            OwnerCount = row.TryGetValue("OwnerCount", out var ownerCount)
                ? int.Parse(ownerCount.N, CultureInfo.InvariantCulture)
                : 0,
        };
    }

    public static Dictionary<string, AttributeValue> ToDynamoDbRow(
        this OrganizationUser membership
    )
    {
        return new Dictionary<string, AttributeValue>
        {
            ["OrganizationId"] = new(membership.OrganizationId.ToString()),
            ["UserId"] = new(membership.UserId.ToString()),
            ["Role"] = new(membership.Role.ToString()),
            ["CreatedByUserId"] = new(membership.CreatedByUserId.ToString()),
            ["UpdatedByUserId"] = new(membership.UpdatedByUserId.ToString()),
            ["CreatedAt"] = new(membership.CreatedAt.ToString("O")),
            ["UpdatedAt"] = new(membership.UpdatedAt.ToString("O")),
        };
    }

    public static OrganizationUser ToOrganizationUser(
        this Dictionary<string, AttributeValue> row
    )
    {
        return new OrganizationUser
        {
            OrganizationId = Guid.Parse(row["OrganizationId"].S),
            UserId = Guid.Parse(row["UserId"].S),
            Role = Enum.Parse<OrganizationRole>(row["Role"].S),
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
