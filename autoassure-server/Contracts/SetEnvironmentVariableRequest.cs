using System.ComponentModel.DataAnnotations;

namespace A2.Server.Contracts;

/// <summary>Request body to upsert a single Environment variable's value.</summary>
public record SetEnvironmentVariableRequest([Required, MaxLength(4000)] string Value);
