using A2.Server.Models;
using Environment = A2.Server.Models.Environment;

namespace A2.Server.UnitTests;

/// <summary>
///     Unit tests for <see cref="RunEnvironmentSnapshot.FromEnvironment" /> and
///     <see cref="RunEnvironmentSnapshot.Masked" />.
/// </summary>
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
        var snapshot = RunEnvironmentSnapshot.FromEnvironment(
            environment,
            variables
        );

        // verify
        Assert.Equal(environment.Id, snapshot.Source.Id);
        Assert.Equal(
            environment.CreatedByUserId,
            snapshot.Source.CreatedByUserId
        );
        Assert.Equal(
            environment.UpdatedByUserId,
            snapshot.Source.UpdatedByUserId
        );
        Assert.Equal(environment.CreatedAt, snapshot.Source.CreatedAt);
        Assert.Equal(environment.UpdatedAt, snapshot.Source.UpdatedAt);
        Assert.Equal(environment.Name, snapshot.Name);
        Assert.Equal(environment.Classification, snapshot.Classification);
        Assert.Same(variables, snapshot.Variables);
    }

    private static RunEnvironmentSnapshot CreateSnapshotWithVariables(
        Guid environmentId,
        string sensitiveValue,
        string plainValue
    )
    {
        return RunEnvironmentSnapshot.FromEnvironment(
            new Environment
            {
                Id = environmentId,
                OrganizationId = Guid.NewGuid(),
                ApplicationId = Guid.NewGuid(),
                Name = "Staging",
                Classification = EnvironmentClassification.NonProduction,
                CreatedByUserId = Guid.NewGuid(),
                UpdatedByUserId = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z"),
            },
            [
                RunEnvironmentVariableSnapshot.FromEnvironmentVariable(
                    new EnvironmentVariable
                    {
                        OrganizationId = Guid.NewGuid(),
                        EnvironmentId = environmentId,
                        Key = "API_KEY",
                        Value = sensitiveValue,
                        IsSensitive = true,
                        CreatedByUserId = Guid.NewGuid(),
                        UpdatedByUserId = Guid.NewGuid(),
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow,
                    }
                ),
                RunEnvironmentVariableSnapshot.FromEnvironmentVariable(
                    new EnvironmentVariable
                    {
                        OrganizationId = Guid.NewGuid(),
                        EnvironmentId = environmentId,
                        Key = "BASE_URL",
                        Value = plainValue,
                        IsSensitive = false,
                        CreatedByUserId = Guid.NewGuid(),
                        UpdatedByUserId = Guid.NewGuid(),
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow,
                    }
                ),
            ]
        );
    }

    [Fact]
    public void
        Masked_WhenVariablesMixSensitiveAndPlain_MasksOnlyTheSensitiveOne()
    {
        // setup
        var snapshot = CreateSnapshotWithVariables(
            Guid.NewGuid(),
            "abcdefghijklmnopqrstuvwxyz",
            "https://staging.example.com"
        );

        // test
        var masked = snapshot.Masked();

        // verify
        var maskedSensitive = Assert.Single(
            masked.Variables,
            v => v.Key == "API_KEY"
        );
        Assert.Equal(
            SensitiveValueMasker.Mask("abcdefghijklmnopqrstuvwxyz"),
            maskedSensitive.Value
        );
        var untouchedPlain = Assert.Single(
            masked.Variables,
            v => v.Key == "BASE_URL"
        );
        Assert.Equal("https://staging.example.com", untouchedPlain.Value);
    }

    [Fact]
    public void Masked_WhenCalledTwice_IsIdempotent()
    {
        // setup
        var snapshot = CreateSnapshotWithVariables(
            Guid.NewGuid(),
            "abcdefghijklmnopqrstuvwxyz",
            "https://staging.example.com"
        );

        // test
        var maskedOnce = snapshot.Masked();
        var maskedTwice = maskedOnce.Masked();

        // verify
        Assert.Equal(
            maskedOnce.Variables.Select(v => v.Value),
            maskedTwice.Variables.Select(v => v.Value)
        );
    }
}