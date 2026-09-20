namespace A2.Server.Models;

public record RunEnvironmentVariableSnapshot
{
    public required string Key { get; init; }

    /// <summary>
    ///     The raw, unmasked value.
    /// </summary>
    public required string Value { get; init; }

    public required bool IsSensitive { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required Guid UpdatedByUserId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }

    public static RunEnvironmentVariableSnapshot FromEnvironmentVariable(
        EnvironmentVariable environmentVariable
    )
    {
        return new RunEnvironmentVariableSnapshot
        {
            Key = environmentVariable.Key,
            Value = environmentVariable.Value,
            IsSensitive = environmentVariable.IsSensitive,
            CreatedByUserId = environmentVariable.CreatedByUserId,
            UpdatedByUserId = environmentVariable.UpdatedByUserId,
            CreatedAt = environmentVariable.CreatedAt,
            UpdatedAt = environmentVariable.UpdatedAt,
        };
    }

    /// <summary>
    ///     Return a copy of this environment variable snapshot with the sensitive
    ///     value masked.
    /// </summary>
    public RunEnvironmentVariableSnapshot Masked()
    {
        return IsSensitive
            ? this with
            {
                Value = SensitiveValueMasker.Mask(Value),
            }
            : this;
    }
}