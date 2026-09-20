using A2.Server.Models;

namespace A2.Server.UnitTests;

/// <summary>Unit tests for <see cref="RunActivitySnapshot.FromActivity" />.</summary>
public sealed class RunActivitySnapshotTests
{
    [Fact]
    public void FromActivity_WhenGivenALiveActivity_CopiesEveryField()
    {
        // setup
        var activity = new Activity
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            ApplicationId = Guid.NewGuid(),
            ScenarioId = Guid.NewGuid(),
            Description = "Submit the checkout form",
            Order = 3,
            PreconditionIds = [Guid.NewGuid()],
            EvidenceIds = [Guid.NewGuid()],
            CreatedByUserId = Guid.NewGuid(),
            UpdatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z"),
        };
        IReadOnlyList<RunPreconditionSnapshot> preconditions =
        [
            RunPreconditionSnapshot.FromPrecondition(
                new Precondition
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = activity.OrganizationId,
                    ApplicationId = activity.ApplicationId,
                    Name = "Order Confirmation ID",
                    ValueSource = PreconditionValueSource.SpecificValue,
                    ExampleValue = "ORD-12345",
                    CreatedByUserId = Guid.NewGuid(),
                    UpdatedByUserId = Guid.NewGuid(),
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                }
            ),
        ];
        IReadOnlyList<RunEvidenceDefinitionSnapshot> evidenceDefinitions =
        [
            RunEvidenceDefinitionSnapshot.FromEvidenceDefinition(
                new EvidenceDefinition
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = activity.OrganizationId,
                    ApplicationId = activity.ApplicationId,
                    Name = "Order Confirmation ID",
                    Description = "Captured from the checkout response.",
                    ExampleValue = "ORD-12345",
                    CreatedByUserId = Guid.NewGuid(),
                    UpdatedByUserId = Guid.NewGuid(),
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                }
            ),
        ];

        // test
        var snapshot = RunActivitySnapshot.FromActivity(
            activity,
            preconditions,
            evidenceDefinitions
        );

        // verify
        Assert.Equal(activity.Id, snapshot.Source.Id);
        Assert.Equal(activity.CreatedByUserId, snapshot.Source.CreatedByUserId);
        Assert.Equal(activity.UpdatedByUserId, snapshot.Source.UpdatedByUserId);
        Assert.Equal(activity.CreatedAt, snapshot.Source.CreatedAt);
        Assert.Equal(activity.UpdatedAt, snapshot.Source.UpdatedAt);
        Assert.Equal(activity.Order, snapshot.Order);
        Assert.Equal(activity.Description, snapshot.Description);
        Assert.Same(preconditions, snapshot.Preconditions);
        Assert.Same(evidenceDefinitions, snapshot.EvidenceDefinitions);
    }
}