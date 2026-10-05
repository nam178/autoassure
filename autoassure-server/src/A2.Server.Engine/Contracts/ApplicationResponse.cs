namespace A2.Server.Engine.Contracts;

// ReSharper disable NotAccessedPositionalProperty.Global -- serialized to the JSON response body, not read in-process
/// <summary>An Application, as returned to the client.</summary>
public record ApplicationResponse(Guid Id, string Name, string Description);
