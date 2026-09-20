using A2.Server.Models;

namespace A2.Server.UnitTests;

/// <summary>
///     Unit tests for <see cref="RunPreconditionSnapshot.FromPrecondition" />
///     .
/// </summary>
public sealed class RunPreconditionSnapshotTests
{
    [Fact]
    public void FromPrecondition_WhenGivenALivePrecondition_CopiesEveryField()
    {
        // setup
        var precondition = new Precondition
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            ApplicationId = Guid.NewGuid(),
            Name = "Order Confirmation ID",
            ValueSource = PreconditionValueSource.SpecificValue,
            ExampleValue = "ORD-12345",
            CreatedByUserId = Guid.NewGuid(),
            UpdatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z"),
        };

        // test
        var snapshot = RunPreconditionSnapshot.FromPrecondition(precondition);

        // verify
        Assert.Equal(precondition.Id, snapshot.Source.Id);
        Assert.Equal(
            precondition.CreatedByUserId,
            snapshot.Source.CreatedByUserId
        );
        Assert.Equal(
            precondition.UpdatedByUserId,
            snapshot.Source.UpdatedByUserId
        );
        Assert.Equal(precondition.CreatedAt, snapshot.Source.CreatedAt);
        Assert.Equal(precondition.UpdatedAt, snapshot.Source.UpdatedAt);
        Assert.Equal(precondition.Name, snapshot.Name);
        Assert.Equal(precondition.ValueSource, snapshot.ValueSource);
        Assert.Equal(precondition.ExampleValue, snapshot.ExampleValue);
    }
}