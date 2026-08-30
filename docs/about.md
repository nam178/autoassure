## Problem Statement

> "“tests become another software system that we have to maintain”

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
entirely. Users describe _what_ should be verified; AutoAssure determines _how_
to execute it. The result: test coverage that evolves with your software instead
of fighting against it.

This is made possible by leveraging AI—not to write better test scripts, but to
eliminate the test-script abstraction itself. AI agents discover what needs
testing, maintain a living knowledge base of test behaviors, and dynamically
work out execution order, state acquisition, and cleanup. Engineers focus on
verifying software behavior; the machine handles the infrastructure.

## Glossary

**Organization**. AutoAssure is a multi-tenant SaaS platform. Each user belongs
to one or more AutoAssure customers, called Organization. Users can create
organizations for their company and invite other users.

**Scenario** The primary entity users create and manage. A description of a
specific software behavior that AutoAssure should verify, made up of one or more
**Activities**.

**Activity** A meaningful step within a Scenario. An Activity is not necessarily
an assertion—it can describe an action, behavior, verification, state
transition, or other meaningful part of a Scenario. Activities execute
top-to-bottom by default.

**Precondition** A named requirement or piece of context an Activity needs in
order to execute. Preconditions describe _what is required_, not setup
instructions, and are drawn from a reusable library where possible.

**Evidence** Information AutoAssure collects from an Activity. Evidence can
prove the Activity's outcome, help diagnose failures, or provide information for
subsequent execution—it is not merely a success assertion. Evidence is scoped to
a Run by default.

**Run** The result of trying one or more Scenarios against a specific
Environment at a specific point in time.

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

### 0. Core philosophy

Users control:

- What behavior they want verified.
- What a Scenario or Activity means.
- Important requirements when AutoAssure cannot infer them.
- Important Evidence when automatic success evaluation is insufficient.
- Environment/context.
- Explicit dependencies, in exceptional cases.

AutoAssure controls:

- Test-data creation.
- State acquisition.
- Execution mechanics and ordering.
- Dependency resolution.
- Whether to continue after failures.
- Failure diagnosis.
- Cleanup.
- How the behavior is actually exercised.

The central principle: **users describe what should be verified, AutoAssure
determines how to verify it.**

The most important architectural invariant: an Activity should be independently
executable when possible, but AutoAssure may reuse existing state when doing so
is safe and useful. An Activity should never _accidentally_ depend on state
merely because another Activity happened to run before it—that would quietly
rebuild the brittle test-dependency problem AutoAssure exists to eliminate.

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
with test design—for example, a Discovery Agent that inspects your source code
and suggests Scenarios. You can turn this on or off per Application in Settings.
Everything an Agent does, you can also do yourself.

### 3. Describe what to test

You create **Scenarios** that describe the behaviors you want to verify, made up
of one or more **Activities**. You can write Scenarios manually, or let a
Discovery Agent suggest them based on your source code.

An Activity should preferably be independently executable, but this is a
guideline, not a requirement—not every Activity needs to be isolated.

> **Tip:** Design each Activity so it can be executed independently when
> possible. This makes failures easier to diagnose and helps AutoAssure recover
> when a previous Activity fails.

> **Under the hood:** AutoAssure analyses each Activity to determine what it
> needs in order to run (**Preconditions**) and what it produces (**Evidence**).
> You can see these on a Scenario's detail page, but you don't need to define
> them yourself—AutoAssure first attempts to discover or acquire the values an
> Activity needs on its own.

### 4. Preconditions and Evidence, when you need them

Most Scenarios never require you to touch Preconditions or Evidence directly—
AutoAssure attempts to satisfy an Activity's requirements automatically.
Explicit Preconditions are an escape hatch for when AutoAssure cannot infer
what's needed, not the normal workflow. The product actively discourages adding
unnecessary explicit Preconditions.

When you do need one, you can select an existing entry from a reusable
**Precondition library** instead of typing it out again—for example,
`Authenticated User: The current user has logged in.` Preconditions can also be
sourced **from prior Activities** within the same Scenario.

Similarly, Evidence is normally inferred. You only define it explicitly when
automatic success evaluation isn't enough, or when later Activities need
specific information from an earlier one.

### 5. Configure your Environments

You set up **Environments** (e.g., Staging, Production) as collections of
key-value pairs—API endpoints, credentials, feature flags—that Scenarios
reference at runtime. You mark each Environment as Production or Non-Production.
Destructive Activities (e.g., deleting test data) are automatically excluded
from Production Environments.

If a Scenario needs a value that doesn't exist yet, the system prompts you to
add it to the relevant Environment on the fly.

### 6. Let AutoAssure decide execution order

Activities within a Scenario execute top-to-bottom by default. There are no
configurable continue / skip / abort / retry strategies to set up—AutoAssure
decides whether a failed Activity should prevent subsequent Activities from
running, based on the failed Activity, the subsequent Activity, and their
requirements.

Every such decision is shown to you in the Run, for example:

> "Password length check failed, but the failure does not prevent authentication
> checks from running, so execution continued."

Scenarios may also declare explicit dependencies on other Scenarios, but this is
an advanced escape hatch, not normal usage—AutoAssure determines how to satisfy
a Scenario's requirements itself wherever it can. When you do declare a
dependency, you choose:

- **Once** — Reuse the dependency's successful result within the current Run
  when possible.
- **Fresh** — Execute the dependency again even if a suitable result already
  exists.

A Scenario dependency represents required state or capability, not merely "this
must run before that." The underlying dependency graph is an internal
implementation detail—you never need to design it by hand.

AutoAssure does not offer before-each, after-each, before-all, after-all,
fixtures, or similar hooks. If you want behavior that would traditionally be
represented by a hook, describe the requirement in the Scenario or Activity and
let AutoAssure determine how to achieve it.

### 7. Try or Run, and inspect results

You can **Try** a single Scenario directly, or start a **Run** against one or
more Scenarios targeting a specific Environment. When starting a Run, you select
the Test Client that should execute the operations.

For convenience, every Application has a default Test Client hosted by
AutoAssure. This is suitable when AutoAssure can directly access the target
Environment. For environments that are only accessible from your own network,
you can install a Test Client in your environment and select it when running.

If the selected Environment requires a Test Client that isn't available,
AutoAssure guides you through downloading and connecting one.

The Test Client performs the requested operations on behalf of AutoAssure while
enforcing its configured permissions. It can run temporarily as part of a CI/CD
pipeline or continuously as a service for on-demand execution.

Each Run captures the concrete Evidence values from every Activity, AutoAssure's
reasoning behind any continue/stop decisions, and full tracing and structured
logs—HTTP requests, response bodies, timing, and errors. You browse recent Runs
from your Application overview to inspect results without needing to configure
any external logging or tracing infrastructure.

### 8. Cleanup

AutoAssure automatically tracks and cleans up the resources and data it creates
during a Run, rather than requiring you to write teardown logic. Where cleanup
configuration is exposed, you can choose:

- **Always** (default)
- **Only when successful**

Cleanup is an advanced concern, not something you need to manage by default—and
AutoAssure is careful that a failed Run doesn't leave unwanted test data behind.

### 9. Organize your Scenarios

As your library of Scenarios grows, AutoAssure helps you browse and manage them
without relying on manually maintained tags or a hand-built folder hierarchy.
**Auto Folders** are inferred automatically from each Scenario's description,
and you can override the folder if needed. Folder organization is purely
organizational metadata—it has no effect on execution.

You can also manage scenarios by tags. You can tags scenarios automatically, or
let AutoAssure choose the right tags for you.
