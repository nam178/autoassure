using A2.Server.Common;

namespace A2.Server.UserManagement.Models;

/// <summary>
/// A tenant in AutoAssure's multi-tenant SaaS. Every User belongs to at least
/// one, via
/// <see cref="OrganizationUser" />. A personal Organization is auto-created on
/// a user's first sign-in.
/// </summary>
public record Organization
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required bool IsPersonal { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required Guid UpdatedByUserId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// The lifecycle state of this Organization. An Organization only ever holds
    /// <see cref="LifecycleState.Active" /> or
    /// <see cref="LifecycleState.Archived" />; it is never purged,
    /// so <see cref="LifecycleState.Deleting" /> does not apply.
    /// </summary>
    public required LifecycleState LifecycleState { get; init; }

    /// <summary>Number of Owners currently in this Organization.</summary>
    public required int OwnerCount { get; init; }
}
