namespace A2.Server.Models;

/// <summary>An EnvironmentVariable copied into a Run at create time, so a run's record of what it ran
/// against never changes when the variable is edited later. A sensitive value is masked before it enters
/// the snapshot -- run history holds the mask, never the secret, so rotating a credential needs no
/// cleanup of old runs.</summary>
public record RunEnvironmentVariableSnapshot
{
    public required string Key { get; init; }

    /// <summary>The variable's value, whole for an ordinary variable. For a sensitive one this already is
    /// the masked form -- the real value never enters a Run.</summary>
    public required string Value { get; init; }
    public required bool IsSensitive { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required Guid UpdatedByUserId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Copies an EnvironmentVariable as it is right now, masking the value first when the
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
            Value = environmentVariable.IsSensitive
                ? SensitiveValueMasker.Mask(environmentVariable.Value)
                : environmentVariable.Value,
            IsSensitive = environmentVariable.IsSensitive,
            CreatedByUserId = environmentVariable.CreatedByUserId,
            UpdatedByUserId = environmentVariable.UpdatedByUserId,
            CreatedAt = environmentVariable.CreatedAt,
            UpdatedAt = environmentVariable.UpdatedAt,
        };
}
