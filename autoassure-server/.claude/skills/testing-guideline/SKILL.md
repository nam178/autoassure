---
name: testing-guideline
description: MUST load this BEFORE writing or editing any unit test or integration test in this repo.
---

- Unit tests: pure functions/classes only. No controllers — use ASP.NET integration tests (`WebApplicationFactory`, real HTTP client).
- Integration tests: fake downstream deps (realistic in-memory, no external calls), never mocks/stubs.
- Use realistic test data — no nonsense inputs just to hit a path or coverage number.
- Parameterize (`[Theory]`/`[InlineData]`/`[MemberData]`) for empty/too-long/negative/very-large/boundary cases.
- Duplication across tests is fine — don't extract shared helpers to dedupe.

New APIs: add `[Authorize]`, plus a local integration test proving unauthorized access is rejected.