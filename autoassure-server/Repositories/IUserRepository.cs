using A2.Server.Models;

namespace A2.Server.Repositories;

/// <summary>Persists AutoAssure user accounts. Storage-agnostic — callers only ever see the User domain model.</summary>
public interface IUserRepository
{
    /// <summary>Looks up the user who previously signed in with this Google account, or null if none has.</summary>
    Task<User?> GetByGoogleUserIdAsync(string googleUserId);

    /// <summary>Creates a new user, unless one for the same GoogleUserId was just created concurrently
    /// (e.g. two simultaneous first sign-ins), in which case it does nothing and returns false.</summary>
    Task<bool> TrySaveAsync(User user);

    /// <summary>Updates only FirstName, LastName, Email, and EmailVerified on an existing user.</summary>
    Task UpdateAsync(Guid id, UserUpdatableFields fields);

    /// <summary>Creates the given personal Organization and its owner membership for this user
    /// together, unless this user's personal Organization was just created concurrently (e.g. two
    /// simultaneous sign-ins), in which case it does nothing and returns false.</summary>
    Task<bool> TryCreatePersonalOrganizationAsync(
        Organization organization,
        OrganizationUser membership
    );
}
