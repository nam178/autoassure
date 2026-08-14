## Problem Statement

Modern software testing is fragile and expensive to maintain. Tests are
typically implemented as scripts with hard-coded setup, teardown, dependencies,
and execution order. As systems evolve, these scripts become difficult to
understand and increasingly painful to maintain. Tests often depend on state
created by other tests, creating brittle dependency chains and complicated setup
and cleanup logic. Engineers end up maintaining test infrastructure rather than
focusing on verifying software behavior.

## Solution Overview

AutoAssure is **not another natural-language test generation tool**. It does not
simply turn English descriptions into test scripts.

Instead, AutoAssure introduces a new methodology that eliminates test scripts
entirely. Rather than maintaining brittle code that encodes setup, teardown, and
execution order, teams describe _what_ should be verified and let the system
determine _how_ to execute it. The result: test coverage that evolves with your
software instead of fighting against it.

This is made possible by leveraging AI—not to write better test scripts, but to
eliminate the test-script abstraction itself. AI agents discover what needs
testing, maintain a living knowledge base of test behaviors, and dynamically
generate execution plans. Engineers focus on verifying software behavior; the
machine handles the infrastructure.

## Glossary

**Scenario** A description of a specific software behavior that AutoAssure
should verify.

**Precondition** A named value that a Scenario requires in order to execute.

**Evidence** A named value that a Scenario produces upon successful execution.

**Execution Plan** A natural-language description of a sequence of software
behaviors that AutoAssure should execute and verify. An Execution Plan can
mention Scenarios and other Execution Plans, allowing plans to be composed and
reused.

Execution Plans are versioned and immutable. When an Execution Plan changes, a
new version is created rather than modifying the existing version.

**Run** The result of executing an Execution Plan against a specific Environment
at a specific point in time.

**Environment** A named configuration (e.g., Production, Staging) holding
key-value pairs accessible inside Scenarios. Classified as Production or
Non-Production.

**Agent** An AI worker that autonomously performs work on the Scenario Knowledge
Base.

**Application** A software product or system owned by a team and tested as a
whole.

**Component** A deployable or logically distinct part of an Application
associated with source code (e.g., a microservice or monolith).

**Test Client** A lightweight application that runs in your environment and
allows AutoAssure to execute authorized operations against systems that are not
directly accessible from AutoAssure Cloud.

## How It Works

### 1. Create an Application

On first launch, a setup wizard guides you through creating your first
**Application**—typically one per product or system your team owns. Access
permissions are scoped to Applications, so each team sees only what's theirs.

### 2. Define your Components

Within your Application, you add **Components**—the services or microservices
that make up your system. You link each Component to a code repository and
optionally pin it to a branch or tag. If your system is a monolith, AutoAssure
creates a single default Component for you so you can skip this step and start
testing immediately.

Once your Components are in place, AutoAssure can offer AI **Agents** to help
with test design and planning—for example, a Discovery Agent that inspects your
source code and suggests Scenarios, or an Execution Agent that drafts Execution
Plans. You can turn this on or off per Application in Settings. Everything an
Agent does, you can also do yourself.

### 3. Describe what to test

You create **Scenarios** that describe the behaviors you want to verify.
Scenarios are not scoped to a single Component—they can span your entire system.
You can write Scenarios manually, or let a Discovery Agent suggest them based on
your source code.

> **Under the hood:** AutoAssure analyses each Scenario to determine what it
> needs in order to run (**Preconditions**) and what it produces upon success
> (**Evidence**). You can see these on a Scenario's detail page, but you don't
> need to define them yourself.

### 5. Configure your Environments

You set up **Environments** (e.g., Staging, Production) as collections of
key-value pairs—API endpoints, credentials, feature flags—that Scenarios
reference at runtime. You mark each Environment as Production or Non-Production.
Destructive Scenarios (e.g., deleting test data) are automatically excluded from
Production Environments.

If a Scenario needs a value that doesn't exist yet, the system prompts you to
add it to the relevant Environment on the fly.

### 6. Design your Execution Plans

While Scenarios describe individual behaviors, **Execution Plans** describe
multi-step flows—the order in which Scenarios should run and how they connect.
They are written as simple, natural-language documents rather than test scripts
or visual graphs.

For example:

```text
# Shopping Cart Payment Testing

1. Log in as a customer.
2. Add a MacBook to the shopping cart.
3. Proceed to checkout.
4. Complete payment with a valid credit card.
5. Verify that the order was successfully created.
```

When you save a plan, AutoAssure interprets what you've written and works out
how the Scenarios should be executed, including how Evidence produced by one
step should be used by another. If something is ambiguous, AutoAssure asks you
to clarify rather than requiring you to learn a new test syntax.

Execution Plans can also run other Execution Plans, making them reusable and
composable. For example, a `Prepare Shopping Cart` plan can be reused by
multiple payment-testing plans without duplicating the setup steps.

Plans can describe conditional behavior in natural language as well. AutoAssure
handles the underlying branching and orchestration automatically—the execution
graph is an internal implementation detail, not something you need to design.

You can create and modify Execution Plans yourself, or let an **Execution
Agent** generate and maintain them for you.

> **Under the hood:** When you save an Execution Plan, AutoAssure automatically
> resolves which Scenarios to invoke and how the output of one step feeds into
> the next. The result is a directed execution graph—you can view it after
> saving, but you never need to build or maintain it yourself.

### 7. Run and inspect results

You execute an Execution Plan against a target Environment to produce a **Run**.
When starting a Run, you select the Test Client that should execute the
operations.

For convenience, every Application has a default Test Client hosted by
AutoAssure. This is suitable when AutoAssure can directly access the target
Environment. For environments that are only accessible from your own network,
you can install a Test Client in your environment and select it when running the
plan.

If the selected Environment requires a Test Client that isn't available,
AutoAssure guides you through downloading and connecting one.

The Test Client performs the requested operations on behalf of AutoAssure while
enforcing its configured permissions. It can run temporarily as part of a CI/CD
pipeline or continuously as a service for on-demand execution.

Each Run captures the concrete Evidence values from every Scenario, along with
full tracing and structured logs—HTTP requests, response bodies, timing, and
errors. You browse recent Runs from your Application overview to inspect results
without needing to configure any external logging or tracing infrastructure.

Under the hood: Execution Agents determine which operations are required during
a Run. AutoAssure sends operations that require environment access to the
selected Test Client. The Test Client executes them within its environment,
enforces its local permission policy, and returns the results to AutoAssure.
