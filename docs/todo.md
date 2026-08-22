## Todos

### Logins

1. **High** — Introduce a proper AutoAssure user pool instead of treating the
   Google `sub` as the app's user ID. Right now `AuthTokenService` puts
   `identity.GoogleUserId` straight into the JWT `sub` claim — there's no
   AutoAssure-owned user record. Add a users table that maps a Google identity
   to an AutoAssure user ID, mint JWTs with that internal ID, and look up/create
   the mapping during `IssueAsync`. This is what unlocks adding other login
   providers later, linking multiple identities to one account, and not having
   to re-key everything if a Google account changes.
1. **Medium** — Auto-refresh auth tokens before they expire. If a refresh fails,
   sign the user out immediately so the UI can show a "you've been logged out"
   screen.

### Security

1. **High** — Add breach detection for reused refresh tokens: track refresh
   tokens by user (needs a GSI on `GoogleUserId` in the refresh token table) and
   add a `RevokeAllForUserAsync` repository method. When
   `AuthTokenService.RefreshAsync` sees a token that's already revoked (as
   opposed to just missing/expired), treat that as a sign of theft and revoke
   every refresh token for that user, not just return a generic 401.
2. **Medium** — Enforce `EmailVerified` on Google sign-in.
   `GoogleTokenExchangeService` already captures `EmailVerified` from Google's
   ID token, but `AuthTokenService.IssueAsync` never checks it before issuing
   tokens. Confirm whether anything downstream trusts the `Email` claim for
   authorization (invites, domain restrictions, etc.) and if so, reject sign-in
   when the email isn't verified.
3. **Medium** — Add rate limiting to the unauthenticated auth endpoints
   (`POST auth/google/token` and `POST auth/refresh`) to reduce
   brute-force/credential-stuffing exposure.

### Create new test flow

1. **High** — Implement the "create new test flow" feature — this is one of the
   core features and will teach us a lot about how AutoAssure should work.
