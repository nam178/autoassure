using A2.Server.Models;
using Environment = A2.Server.Models.Environment;

namespace A2.Server.UnitTests;

/// <summary>Unit tests for <see cref="RunEnvironmentSnapshot.FromEnvironment"/>.</summary>
public sealed class RunEnvironmentSnapshotTests
{
    [Fact]
    public void FromEnvironment_WhenGivenALiveEnvironment_CopiesEveryField()
    {
        // setup
        var environment = new Environment
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            ApplicationId = Guid.NewGuid(),
            Name = "Staging",
            Classification = EnvironmentClassification.NonProduction,
            CreatedByUserId = Guid.NewGuid(),
            UpdatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z"),
        };
        IReadOnlyList<RunEnvironmentVariableSnapshot> variables =
        [
            RunEnvironmentVariableSnapshot.FromEnvironmentVariable(
                new EnvironmentVariable
                {
                    OrganizationId = environment.OrganizationId,
                    EnvironmentId = environment.Id,
                    Key = "BASE_URL",
                    Value = "https://staging.example.com",
                    CreatedByUserId = Guid.NewGuid(),
                    UpdatedByUserId = Guid.NewGuid(),
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    IsSensitive = false,
                }
            ),
        ];

        // test
        var snapshot = RunEnvironmentSnapshot.FromEnvironment(environment, variables);

        // verify
        Assert.Equal(environment.Id, snapshot.Source.Id);
        Assert.Equal(environment.CreatedByUserId, snapshot.Source.CreatedByUserId);
        Assert.Equal(environment.UpdatedByUserId, snapshot.Source.UpdatedByUserId);
        Assert.Equal(environment.CreatedAt, snapshot.Source.CreatedAt);
        Assert.Equal(environment.UpdatedAt, snapshot.Source.UpdatedAt);
        Assert.Equal(environment.Name, snapshot.Name);
        Assert.Equal(environment.Classification, snapshot.Classification);
        Assert.Same(variables, snapshot.Variables);
    }
}
