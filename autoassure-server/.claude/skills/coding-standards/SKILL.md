---
name: coding-standards
description:
  MUST load this BEFORE writing or editing any code file, or PLANNING code
  changes.
---

# General

- MUST use inline variables when possible.
- MUST use immutable types when possible.
- MUST declare request/response type in controllers (Task<IActionResult> is too ambiguous)
- MUST not use ! operator. Throw instead.
- Class and function names MUST be specific. Good: ToDynamoDbRow (), class ShippingRules. Bad: ToItem (), class
  BusinessUtils.

# Documentation

- MUST add docs for interfaces, classes, and methods. Skip for impls behind an interface. Format: What do they
  represent? When to use it? MUST keep it simple, 1-2. sentences.
- MUST add SHORT inline comment (1-2 sentence) for each code block. Good: "// Add user to the database. No need to check
  the result because.." (explained the INTENTION, then the WHY).
- If a function/method/interfaces throws, MUST document the throw, including throws from downstream services. BOTH in
  interfaces and implementation.
- When invoking functions with documented throw: 1/ avoid invoking it in the way that cause it to throw. 2/
  Catch-Wrap-log-Rethrow 3/ Do nothing, but document the throw.
- AVOID throw if possible, express error a compile-time type. Example: "User?"
  means it's null when the user not found. Example 2: TryCreate () returns false when dup.

# Models (Domain Layer)

- Business models are stored in Models folders.
- No external deps, API calls etc.
- Can have pure business logic methods (Good: Order.CalculateOrderDiscount ())
- Add XML doc. DO NOT MENTION anything about infra, storage. Discuss business domain, Good: "Customer can place maximum
  of 3 orders..", Bad: "This model stored in DynamoDB.."

# Repositories (Data Layer)

- Repository methods MUST be dumb, no business logic.
- MUST Hide storage and database implementation details.
- SHOULD reference domain models. Good: AddUser (User); Bad: InsertUser (UserRecord);
- MUST include OrganizationId in every query, as this is multi-tenant SaaS.
- MUST use mapper to convert domain model <-> DynamoDB, including individual fields. Mapping functions located at
  DynampDbMapper.cs, DynamoDbMapper.Application.cs, etc.
- MUST use consistent read by default.
- MUST use naming convention: SaveX () - create or update the object as a whole, UpdateX () - update ONLY certain fields
  (MUST use query to change specific fields and document the fields being changed), GetX (), ListX (). Additional
  "Try" prefix is allowed, like TrySaveX ().
- If an entity has a relationship, like Scenario belongs to App, MUST check if the other entity exist when
  insert/updating with ConditionExpression.
- When deleting a parent, make sure all children are deleted FIRST, if they can't be deleted together in one
  transaction.
- DynamoDB can handle empty string. But can't handle empty string within a set. Watch out.

# Services (Business Logic Layer)

- AVOID indeterminisic code, like DateTimeOffset.UtcNow. Hard to unit-test. Repositories is fine as they are integration
  tested, but strictly MUST NOT use in services code.

# Controllers & Contracts (API Layer)

- MUST be well documented (XML doc) similar to AWS API Docs.
- Non-success HTTP status codes:
    - Must doucment with ProducesResponseType
    - MUST document with XML, specify when they occur, e.g. "Returns 400 when username is shorter than 20 chars".
    - MUST use ErrorResponse for response body, with user friendly message.
- MUST be pure, easy to understand, friendly naming.
- Contract record definitions MUST not reference domain models.
- Contract <-> Model mapping is a Controller job. MUST use extension methods in Controllers/ContractMapper.cs,
  ContractMapper.Environments.cs, etc. (mirrors DynamoDbMapper.cs).
- Must specify operation name, example: [HttpPost(Name = "CreateApplication")]

# Functions & Methods

- Most important parameter FIRST. Good: GetUser (userId, options); Bad: GetUser (options, userId).
- Param name SHOULD match their type. Good: Sync (GoogleIdentity googleIdentity); Bad: Sync (GoogleIdentity identity).
- MUST start with verb.
