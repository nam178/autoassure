using A2.Server.Models;
using A2.Server.Repositories;

namespace A2.Server.Services;

public class GoogleUserSyncService(IUserRepository repository) : IGoogleUserSyncService
{
    public async Task<User> SyncAsync(GoogleIdentity googleIdentity)
    {
        var isGoogleEmailReallyVerified = googleIdentity.IsEmailReallyVerified();
        var existing = await repository.GetByGoogleUserIdAsync(googleIdentity.GoogleUserId);

        if (existing is null)
        {
            var newUser = new User
            {
                Id = Guid.NewGuid().ToString(),
                GoogleUserId = googleIdentity.GoogleUserId,
                FirstName = googleIdentity.FirstName ?? "",
                LastName = googleIdentity.LastName ?? "",
                Email = googleIdentity.Email,
                EmailVerified = isGoogleEmailReallyVerified,
            };

            // Add the user to the database. Ignore the result of TryCreateAsync():
            // In a race condition, whoever writes first win, we don't care.
            await repository.TryCreateAsync(newUser);
            return newUser;
        }

        var updatedUser = existing with
        {
            FirstName = googleIdentity.FirstName ?? "",
            LastName = googleIdentity.LastName ?? "",
            Email = googleIdentity.Email,
            EmailVerified = existing.EmailVerified || isGoogleEmailReallyVerified,
        };
        await repository.UpdateAsync(updatedUser);
        return updatedUser;
    }
}
