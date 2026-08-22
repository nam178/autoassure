namespace A2.Server.Models;

/// <param name="EmailVerified">
/// True if Google confirmed the email. Only trust this when <see cref="HostedDomain"/> is set
/// (a Google Workspace account, verified by the domain admin) or <see cref="Email"/> ends with
/// "@gmail.com" (Google-owned, self-verified). For any other domain the address may belong to a
/// non-Google mailbox that was never actually confirmed to receive mail — don't rely on it.
/// </param>
/// <param name="HostedDomain">The Google Workspace domain ("hd" claim), or null for a personal account.</param>
public record GoogleIdentity(
    string GoogleUserId,
    string Email,
    bool EmailVerified,
    string? FirstName,
    string? LastName,
    string? HostedDomain
)
{
    /// <summary>Whether the email is provably verified per the <see cref="EmailVerified"/> rules above.</summary>
    public bool IsEmailReallyVerified() =>
        EmailVerified
        && (
            HostedDomain is not null
            || Email.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase)
        );
}
