---
name: coding-standards
description: MUST load this BEFORE writing or editing any .cs file in this repo.
---

- MUST use inline variables as much as possible.
- MUST use immutable record types as default.
- MUST declare request/response type in controllers (Task<IActionResult> is too ambiguous)
- Repository methods MUST be dumb and direct. no business logic.