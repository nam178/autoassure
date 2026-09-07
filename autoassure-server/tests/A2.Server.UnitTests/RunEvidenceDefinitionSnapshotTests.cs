using A2.Server.Models;

namespace A2.Server.UnitTests;

/// <summary>Unit tests for <see cref="RunEvidenceDefinitionSnapshot.FromEvidenceDefinition"/>.</summary>
public sealed class RunEvidenceDefinitionSnapshotTests
{
    [Fact]
    public void FromEvidenceDefinition_WhenGivenALiveEvidenceDefinition_CopiesEveryField()
    {
        // setup
        var evidenceDefinition = new EvidenceDefinition
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            ApplicationId = Guid.NewGuid(),
            Name = "Order Confirmation ID",
            Description = "The order confirmation id returned by the checkout API.",
            ExampleValue = "ORD-12345",
            CreatedByUserId = Guid.NewGuid(),
            UpdatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z"),
        };

        // test
        var snapshot = RunEvidenceDefinitionSnapshot.FromEvidenceDefinition(evidenceDefinition);

        // verify
        Assert.Equal(evidenceDefinition.Id, snapshot.Source.Id);
        Assert.Equal(evidenceDefinition.CreatedByUserId, snapshot.Source.CreatedByUserId);
        Assert.Equal(evidenceDefinition.UpdatedByUserId, snapshot.Source.UpdatedByUserId);
        Assert.Equal(evidenceDefinition.CreatedAt, snapshot.Source.CreatedAt);
        Assert.Equal(evidenceDefinition.UpdatedAt, snapshot.Source.UpdatedAt);
        Assert.Equal(evidenceDefinition.Name, snapshot.Name);
        Assert.Equal(evidenceDefinition.Description, snapshot.Description);
        Assert.Equal(evidenceDefinition.ExampleValue, snapshot.ExampleValue);
    }
}
