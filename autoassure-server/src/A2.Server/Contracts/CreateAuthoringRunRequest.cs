namespace A2.Server.Contracts;

/// <summary>Request body to Try a single Scenario against an Environment. The Scenario comes from the
/// URL (<c>POST /scenarios/{id}/runs</c>); this only supplies the Environment to run against.</summary>
public record CreateAuthoringRunRequest
{
    public required Guid EnvironmentId { get; init; }
}
