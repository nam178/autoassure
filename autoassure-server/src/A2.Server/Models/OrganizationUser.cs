namespace A2.Server.Models;

/// <summary>A membership linking a User to an Organization. Supports multi-org membership from day
/// one, even though the current sign-in flow only ever creates one row per user (for their personal
/// Organization).</summary>
public record OrganizationUser
{
    public required Guid OrganizationId { get; init; }
    public required Guid UserId { get; init; }
    public required OrganizationRole Role { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required Guid UpdatedByUserId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>A member's permission level within an Organization.</summary>
public enum OrganizationRole
{
    Owner,
    Member,
}
