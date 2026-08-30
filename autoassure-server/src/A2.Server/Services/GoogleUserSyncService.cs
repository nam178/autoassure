using A2.Server.Common;
using A2.Server.Models;
using A2.Server.Repositories;

namespace A2.Server.Services;

public class GoogleUserSyncService(
    IUserRepository userRepository,
    IOrganizationUserRepository organizationUserRepository,
    IClock clock
) : IGoogleUserSyncService
{
    public async Task<User> SyncAsync(GoogleIdentity googleIdentity)
    {
        var user = await SyncUserAsync(googleIdentity);

        // Every user needs at least one Organization. Checked on every sign-in, not just the first,
        // so a user left without one (e.g. a crash between creating the User and its personal
        // Organization) gets backfilled on their next sign-in instead of staying stuck forever.
        var memberships = await organizationUserRepository.ListByUserAsync(user.Id);
        if (memberships.Count == 0)
        {
            await CreatePersonalOrganizationAsync(user);
        }

        return user;
    }

    private async Task<User> SyncUserAsync(GoogleIdentity googleIdentity)
    {
        var isGoogleEmailReallyVerified = googleIdentity.IsEmailReallyVerified();
        var existing = await userRepository.GetByGoogleUserIdAsync(googleIdentity.GoogleUserId);

        if (existing is null)
        {
            var newUser = new User
            {
                Id = Guid.CreateVersion7(),
                GoogleUserId = googleIdentity.GoogleUserId,
                FirstName = googleIdentity.FirstName ?? "",
                LastName = googleIdentity.LastName ?? "",
                Email = googleIdentity.Email,
                EmailVerified = isGoogleEmailReallyVerified,
            };

            // Add the user to the database. If a concurrent first sign-in already won the race,
            // re-fetch the row it created instead so both requests converge on the same User.
            var created = await userRepository.TrySaveAsync(newUser);
            return created
                ? newUser
                : await userRepository.GetByGoogleUserIdAsync(googleIdentity.GoogleUserId)
                    ?? newUser;
        }

        var fields = new UserUpdatableFields
        {
            FirstName = googleIdentity.FirstName ?? "",
            LastName = googleIdentity.LastName ?? "",
            Email = googleIdentity.Email,
            EmailVerified = existing.EmailVerified || isGoogleEmailReallyVerified,
        };
        await userRepository.UpdateAsync(existing.Id, fields);
        return existing with
        {
            FirstName = fields.FirstName,
            LastName = fields.LastName,
            Email = fields.Email,
            EmailVerified = fields.EmailVerified,
        };
    }

    // Auto-creates a personal Organization (IsPersonal=true) and links the user to it as its Owner.
    private async Task CreatePersonalOrganizationAsync(User user)
    {
        var now = clock.UtcNow;
        var organization = new Organization
        {
            Id = Guid.CreateVersion7(),
            Name = $"{user.FirstName} {user.LastName}".Trim(),
            IsPersonal = true,
            CreatedByUserId = user.Id,
            UpdatedByUserId = user.Id,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var membership = new OrganizationUser
        {
            OrganizationId = organization.Id,
            UserId = user.Id,
            Role = OrganizationRole.Owner,
            CreatedByUserId = user.Id,
            UpdatedByUserId = user.Id,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // Ignore the result: if a concurrent sign-in for this same user already won the race and
        // created a personal Organization, we don't need another one.
        await userRepository.TryCreatePersonalOrganizationAsync(organization, membership);
    }
}
