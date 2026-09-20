using A2.Server.Models;
using A2.Server.Repositories;
using Amazon.DynamoDBv2.Model;

namespace A2.Server.UnitTests;

/// <summary>
///     Unit tests for the Organization row mapper in
///     <c>DynamoDbMapper.Organization.cs</c>:
///     the Organization converted to a DynamoDB attribute map and back.
/// </summary>
public sealed class DynamoDbMapperOrganizationTests
{
    private static Organization SampleOrganization()
    {
        return new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Acme Corp",
            IsPersonal = false,
            CreatedByUserId = Guid.NewGuid(),
            UpdatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-01-02T00:00:00Z"),
            LifecycleState = LifecycleState.Active,
        };
    }

    [Fact]
    public void Row_WhenRoundTripped_PreservesEveryField()
    {
        // setup
        var organization = SampleOrganization();

        // test
        var row = organization.ToDynamoDbRow();
        var roundTripped = row.ToOrganization();

        // verify
        Assert.Equal(organization.Id, roundTripped.Id);
        Assert.Equal(organization.Name, roundTripped.Name);
        Assert.Equal(organization.IsPersonal, roundTripped.IsPersonal);
        Assert.Equal(
            organization.CreatedByUserId,
            roundTripped.CreatedByUserId
        );
        Assert.Equal(
            organization.UpdatedByUserId,
            roundTripped.UpdatedByUserId
        );
        Assert.Equal(organization.CreatedAt, roundTripped.CreatedAt);
        Assert.Equal(organization.UpdatedAt, roundTripped.UpdatedAt);
        Assert.Equal(organization.LifecycleState, roundTripped.LifecycleState);
    }

    [Fact]
    public void Row_WhenLifecycleStateIsArchived_RoundTripsArchived()
    {
        // setup
        var organization = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Archived Org",
            IsPersonal = false,
            CreatedByUserId = Guid.NewGuid(),
            UpdatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-01-02T00:00:00Z"),
            LifecycleState = LifecycleState.Archived,
        };

        // test
        var row = organization.ToDynamoDbRow();
        var roundTripped = row.ToOrganization();

        // verify
        Assert.Equal(LifecycleState.Archived, roundTripped.LifecycleState);
    }

    [Fact]
    public void Row_WhenMissingLifecycleStateAttribute_DefaultsToActive()
    {
        // setup: a row representing an Organization created before the LifecycleState attribute existed.
        var row = new Dictionary<string, AttributeValue>
        {
            ["Id"] = new(Guid.NewGuid().ToString()),
            ["Name"] = new("Legacy Org"),
            ["IsPersonal"] = new() { BOOL = false },
            ["CreatedByUserId"] = new(Guid.NewGuid().ToString()),
            ["UpdatedByUserId"] = new(Guid.NewGuid().ToString()),
            ["CreatedAt"] = new("2026-01-01T00:00:00Z"),
            ["UpdatedAt"] = new("2026-01-02T00:00:00Z"),
        };

        // test
        var organization = row.ToOrganization();

        // verify
        Assert.Equal(LifecycleState.Active, organization.LifecycleState);
    }

    [Fact]
    public void Row_WhenLifecycleStateIsUnrecognized_ThrowsArgumentException()
    {
        // setup: a row with an invalid LifecycleState value.
        var row = new Dictionary<string, AttributeValue>
        {
            ["Id"] = new(Guid.NewGuid().ToString()),
            ["Name"] = new("Bad Org"),
            ["IsPersonal"] = new() { BOOL = false },
            ["CreatedByUserId"] = new(Guid.NewGuid().ToString()),
            ["UpdatedByUserId"] = new(Guid.NewGuid().ToString()),
            ["CreatedAt"] = new("2026-01-01T00:00:00Z"),
            ["UpdatedAt"] = new("2026-01-02T00:00:00Z"),
            ["LifecycleState"] = new("InvalidState"),
        };

        // test & verify
        Assert.Throws<ArgumentException>(() => row.ToOrganization());
    }
}
