using A2.Server.Models;

namespace A2.Server.Services;

/// <summary>Keeps an AutoAssure User in sync with the Google account that just signed in.</summary>
public interface IGoogleUserSyncService
{
    /// <summary>Creates the user on first sign-in, or refreshes the name and email-verified status of an existing one.</summary>
    Task<User> SyncAsync(GoogleIdentity googleIdentity);
}
