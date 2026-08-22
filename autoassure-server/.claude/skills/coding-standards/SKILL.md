---
name: coding-standards
description: MUST load this BEFORE writing or editing any code file. Also MUST load when planning.
---

# General

- MUST use inline variables as much as possible.
- MUST use immutable record types as default.
- MUST declare request/response type in controllers (Task<IActionResult> is too ambiguous)
- MUST add documentation for classes and methods. What do they represent? When to use it? MUST keep it simple, 1-2
  sentences.
- MUST add SHORT inline comment (1-2 sentence) for each code block. Good: "// Add user to the database. No need to check
  the result because.." (explained the INTENTION, then the WHY).

# Domain Models

- Business models are stored in Models folders.
- No external deps, API calls etc.
- Can have pure business logic methods (Good: Order.CalculateOrderDiscount ())

# Repository

- Repository methods MUST be dumb, no business logic.
- MUST Hide storage and database implementation details.
- SHOULD reference domain models. Good: AddUser (User); Bad: InsertUser (UserRecord);

# Contract (API Models)

- MUST be well documented (XML doc).
- MUST be pure, easy to understand, friendly naming.
- MUST not contain/refernce domain models.

# Functions

- Most important parameter FIRST. Good: GetUser (userId, options);
- Param name SHOULD match their type. Good: Sync (GoogleIdentity googleIdentity); Bad: Sync (GoogleIdentity identity).