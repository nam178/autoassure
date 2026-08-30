namespace A2.Server.Services;

/// <summary>Resolves the authenticated caller's current Organization. This steel thread only ever
/// creates one <c>OrganizationUser</c> row per User (for their personal Organization), so "current"
/// is just "the only one".</summary>
public interface ICallerOrganizationService
{
    /// <summary>The Organization Id of the currently authenticated caller.</summary>
    Task<Guid> GetOrganizationIdAsync();
}
