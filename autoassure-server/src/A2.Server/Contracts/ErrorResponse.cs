namespace A2.Server.Contracts;

// ReSharper disable NotAccessedPositionalProperty.Global -- serialized to the JSON response body, not read in-process
/// <summary>An error, as returned to the client on a non-success response.</summary>
/// <param name="Message">User-friendly message for displaying to the caller.</param>
public record ErrorResponse(string Message);
