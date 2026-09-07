using A2.Server.Models;

namespace A2.Server.UnitTests;

/// <summary>Unit tests for <see cref="RunScenarioSnapshot.FromScenario"/>.</summary>
public sealed class RunScenarioSnapshotTests
{
    [Fact]
    public void FromScenario_WhenGivenALiveScenario_CopiesEveryField()
    {
        // setup
        var scenario = new Scenario
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            ApplicationId = Guid.NewGuid(),
            Title = "Checkout with a saved card",
            Description = "Verifies checkout succeeds when a card is already on file.",
            Folder = "Checkout",
            Tags = ["smoke", "checkout"],
            ActivityCount = 1,
            CreatedByUserId = Guid.NewGuid(),
            UpdatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z"),
        };
        var activity = new Activity
        {
            Id = Guid.NewGuid(),
            OrganizationId = scenario.OrganizationId,
            ApplicationId = scenario.ApplicationId,
            ScenarioId = scenario.Id,
            Description = "Submit the checkout form",
            Order = 1,
            CreatedByUserId = Guid.NewGuid(),
            UpdatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        IReadOnlyList<RunActivitySnapshot> activities =
        [
            RunActivitySnapshot.FromActivity(activity, [], []),
        ];

        // test
        var snapshot = RunScenarioSnapshot.FromScenario(scenario, activities);

        // verify
        Assert.Equal(scenario.Id, snapshot.Source.Id);
        Assert.Equal(scenario.CreatedByUserId, snapshot.Source.CreatedByUserId);
        Assert.Equal(scenario.UpdatedByUserId, snapshot.Source.UpdatedByUserId);
        Assert.Equal(scenario.CreatedAt, snapshot.Source.CreatedAt);
        Assert.Equal(scenario.UpdatedAt, snapshot.Source.UpdatedAt);
        Assert.Equal(scenario.Title, snapshot.Title);
        Assert.Equal(scenario.Description, snapshot.Description);
        Assert.Equal(scenario.Folder, snapshot.Folder);
        Assert.Equal(scenario.Tags, snapshot.Tags);
        Assert.Same(activities, snapshot.Activities);
    }
}
