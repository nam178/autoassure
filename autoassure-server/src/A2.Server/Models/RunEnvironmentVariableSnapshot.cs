namespace A2.Server.Models;

/// <summary>An EnvironmentVariable copied into a Run at create time, so a run's record of what it ran
/// against never changes when the variable is edited later. Holds the real value, sensitive or not, until
/// something masks it -- see <see cref="Masked"/> -- since a future execution agent needs the real
/// credential to connect to systems under test, and needs it from this frozen snapshot rather than the
/// live variable.</summary>
public record RunEnvironmentVariableSnapshot
{
    public required string Key { get; init; }

    /// <summary>The variable's value, whole for both an ordinary and (until masked) a sensitive
    /// variable.</summary>
    public required string Value { get; init; }
    public required bool IsSensitive { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required Guid UpdatedByUserId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Copies an EnvironmentVariable as it is right now, value included whole even when the
    /// variable is sensitive. OrganizationId is left out -- it already lives on the Run header, and a Run
    /// never spans two of either. EnvironmentId is left out -- it is already known from the enclosing
    /// environment snapshot, and every variable in that snapshot belongs to the same
    /// environment.</summary>
    public static RunEnvironmentVariableSnapshot FromEnvironmentVariable(
        EnvironmentVariable environmentVariable
    ) =>
        new()
        {
            Key = environmentVariable.Key,
            Value = environmentVariable.Value,
            IsSensitive = environmentVariable.IsSensitive,
            CreatedByUserId = environmentVariable.CreatedByUserId,
            UpdatedByUserId = environmentVariable.UpdatedByUserId,
            CreatedAt = environmentVariable.CreatedAt,
            UpdatedAt = environmentVariable.UpdatedAt,
        };

    /// <summary>Masks <see cref="Value"/> when this variable is sensitive, leaving it untouched
    /// otherwise. Idempotent: calling it again on the result returns the same snapshot, since
    /// <see cref="SensitiveValueMasker.Mask"/> itself is idempotent.</summary>
    public RunEnvironmentVariableSnapshot Masked() =>
        IsSensitive ? this with { Value = SensitiveValueMasker.Mask(Value) } : this;
}
