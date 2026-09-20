namespace A2.Server.Common;

/// <summary>
///     Marks an endpoint to allow operations against archived Organizations.
///     All AutoAssure API endpoints will return 403 when the caller's organization
///     is archived. This is used for endpoints
///     like unarchiving that need to work even when the Organization is archived.
///     The <see cref="RequireActiveOrganizationFilter" /> reads this attribute and
///     skips the archive check for marked
///     endpoints.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AllowArchivedOrganizationAttribute : Attribute
{
}