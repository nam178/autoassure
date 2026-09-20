namespace A2.Server.Contracts;

// ReSharper disable NotAccessedPositionalProperty.Global -- serialized to the JSON response body, not read in-process
/// <summary>An Organization, as returned to the client.</summary>
public record OrganizationResponse(Guid Id, string Name, bool IsPersonal);