using A2.Server.Models;

namespace A2.Server.UnitTests;

/// <summary>
///     Unit tests for
///     <see cref="RunEnvironmentVariableSnapshot.FromEnvironmentVariable" /> and
///     <see cref="RunEnvironmentVariableSnapshot.Masked" />.
/// </summary>
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
        var snapshot = RunEnvironmentVariableSnapshot.FromEnvironmentVariable(
            variable
        );

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
    public void FromEnvironmentVariable_WhenVariableIsSensitive_StoresTheRealValueUnmasked()
    {
        // setup
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
        var snapshot = RunEnvironmentVariableSnapshot.FromEnvironmentVariable(
            variable
        );

        // verify: a future execution agent needs the real credential from this snapshot, so Create no
        // longer masks it -- only Masked() does, and only from Start Run onward.
        Assert.Equal(variable.Value, snapshot.Value);
        Assert.True(snapshot.IsSensitive);
    }

    [Fact]
    public void Masked_WhenVariableIsSensitive_ReturnsTheMaskedValue()
    {
        // setup
        var snapshot = new RunEnvironmentVariableSnapshot
        {
            Key = "API_KEY",
            Value = "abcdefghijklmnopqrstuvwxyz",
            IsSensitive = true,
            CreatedByUserId = Guid.NewGuid(),
            UpdatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z"),
        };

        // test
        var masked = snapshot.Masked();

        // verify
        Assert.Equal(SensitiveValueMasker.Mask(snapshot.Value), masked.Value);
        Assert.NotEqual(snapshot.Value, masked.Value);
    }

    [Fact]
    public void Masked_WhenVariableIsNotSensitive_LeavesTheValueUntouched()
    {
        // setup
        var snapshot = new RunEnvironmentVariableSnapshot
        {
            Key = "BASE_URL",
            Value = "https://staging.example.com",
            IsSensitive = false,
            CreatedByUserId = Guid.NewGuid(),
            UpdatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z"),
        };

        // test
        var masked = snapshot.Masked();

        // verify
        Assert.Equal(snapshot.Value, masked.Value);
    }

    [Fact]
    public void Masked_WhenCalledTwiceOnASensitiveVariable_IsIdempotent()
    {
        // setup
        var snapshot = new RunEnvironmentVariableSnapshot
        {
            Key = "API_KEY",
            Value = "abcdefghijklmnopqrstuvwxyz",
            IsSensitive = true,
            CreatedByUserId = Guid.NewGuid(),
            UpdatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z"),
        };

        // test
        var maskedOnce = snapshot.Masked();
        var maskedTwice = maskedOnce.Masked();

        // verify
        Assert.Equal(maskedOnce.Value, maskedTwice.Value);
    }
}
