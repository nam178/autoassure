namespace A2.Server.Contracts;

// ReSharper disable NotAccessedPositionalProperty.Global -- serialized to the JSON response body, not read in-process
/// <summary>
///     A single Environment variable, as returned to the client. When IsSensitive
///     is true, Value
///     is masked: only its first 30% of characters, the rest replaced by a
///     fixed-length run of dots.
/// </summary>
public record EnvironmentVariableResponse(
    string Key,
    string Value,
    bool IsSensitive
);
