---
name: testing-guideline
description: MUST load this BEFORE writing or editing any unit test or integration test in this repo.
---

- Unit test MUST target a single pure function or class with well-defined input/output (calculations, pure logic).
- Unit test MUST NOT target controllers. Controllers MUST use ASP.NET integration tests (`WebApplicationFactory`) with a real client sending real HTTP requests.
- Integration test downstream dependencies MUST be fakes, not mocks. A fake is a realistic in-memory implementation with no external calls, not a stub that just returns canned values.
- Test data MUST be realistic. MUST NOT invent nonsense inputs/outputs just to hit a code path or bump coverage.
- MUST use parameterized tests (`[Theory]`/`[InlineData]`/`[MemberData]`) to cover cases: empty string, too-long string, negative number, very large number, boundary values, etc.
- Copy-paste across tests is fine. MUST NOT extract shared/reusable helper functions just to reduce duplication.
- MUST NOT write a test whose only purpose is to change coverage numbers.
