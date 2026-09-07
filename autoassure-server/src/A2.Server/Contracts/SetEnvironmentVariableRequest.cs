using System.ComponentModel.DataAnnotations;

namespace A2.Server.Contracts;

/// <summary>Request body to upsert a single Environment variable's value.</summary>
public record SetEnvironmentVariableRequest
{
    [Required, MaxLength(4000)]
    public required string Value { get; init; }

    /// <summary>When true, the API masks this variable's value on every future read. Defaults to
    /// false.</summary>
    public bool IsSensitive { get; init; }
}
