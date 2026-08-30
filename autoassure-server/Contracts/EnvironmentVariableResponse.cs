namespace A2.Server.Contracts;

// ReSharper disable NotAccessedPositionalProperty.Global -- serialized to the JSON response body, not read in-process
/// <summary>A single Environment variable, as returned to the client.</summary>
public record EnvironmentVariableResponse(string Key, string Value);
