using A2.Server.Models;

namespace A2.Server.UnitTests;

/// <summary>Unit tests for <see cref="RunEnvironmentVariableSnapshot.FromEnvironmentVariable"/>.</summary>
public sealed class RunEnvironmentVariableSnapshotTests
{
    [Fact]
    public void FromEnvironmentVariable_WhenVariableIsNotSensitive_CopiesTheValueWhole()
    {
        // setup
        var variable = new EnvironmentVariable
        {
            OrganizationId = Guid.NewGuid(),
            EnvironmentId = Guid.NewGuid(),
            Key = "BASE_URL",
            Value = "https://staging.example.com",
            IsSensitive = false,
            CreatedByUserId = Guid.NewGuid(),
            UpdatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z"),
        };

        // test
        var snapshot = RunEnvironmentVariableSnapshot.FromEnvironmentVariable(variable);

        // verify: an ordinary value is carried across unmasked, unlike a sensitive one.
        Assert.Equal(variable.Value, snapshot.Value);
        Assert.Equal(variable.Key, snapshot.Key);
        Assert.False(snapshot.IsSensitive);
        Assert.Equal(variable.CreatedByUserId, snapshot.CreatedByUserId);
        Assert.Equal(variable.UpdatedByUserId, snapshot.UpdatedByUserId);
        Assert.Equal(variable.CreatedAt, snapshot.CreatedAt);
        Assert.Equal(variable.UpdatedAt, snapshot.UpdatedAt);
    }

    [Fact]
    public void FromEnvironmentVariable_WhenVariableIsSensitive_MasksTheValueAtFifteenPercent()
    {
        // setup: a value whose first 15% of the fixed 20-character mask length is "abc" (3 characters).
        var variable = new EnvironmentVariable
        {
            OrganizationId = Guid.NewGuid(),
            EnvironmentId = Guid.NewGuid(),
            Key = "API_KEY",
            Value = "abcdefghijklmnopqrstuvwxyz",
            IsSensitive = true,
            CreatedByUserId = Guid.NewGuid(),
            UpdatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z"),
        };

        // test
        var snapshot = RunEnvironmentVariableSnapshot.FromEnvironmentVariable(variable);

        // verify: the snapshot never holds the real secret, and it keeps less than the API's own 30% mask.
        Assert.NotEqual(variable.Value, snapshot.Value);
        Assert.Equal(SensitiveValueMasker.Mask(variable.Value, 0.15), snapshot.Value);
        Assert.True(snapshot.IsSensitive);
    }
}
