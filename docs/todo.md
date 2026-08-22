## Todos

### Create new test flow

1. **High** — Implement the "create new test flow" feature — this is one of the
   core features and will teach us a lot about how AutoAssure should work.

### Security

1. **High** — Add rate limiting to the unauthenticated auth endpoints
   (`POST auth/google/token` and `POST auth/refresh`) to reduce
   brute-force/credential-stuffing exposure.
2. **High** — Add breach detection for reused refresh tokens: track refresh
   tokens by user (needs a GSI on `GoogleUserId` in the refresh token table) and
   add a `RevokeAllForUserAsync` repository method. When
   `AuthTokenService.RefreshAsync` sees a token that's already revoked (as
   opposed to just missing/expired), treat that as a sign of theft and revoke
   every refresh token for that user, not just return a generic 401.
