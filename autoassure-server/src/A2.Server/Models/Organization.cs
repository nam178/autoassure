namespace A2.Server.Models;

/// <summary>A tenant in AutoAssure's multi-tenant SaaS. Every User belongs to at least one, via
/// <see cref="OrganizationUser"/>. A personal Organization is auto-created on a user's first sign-in.</summary>
public record Organization
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required bool IsPersonal { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required Guid UpdatedByUserId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
