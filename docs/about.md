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
should verify. Each Scenario declares its Inputs (what it needs to run) and
Outputs (what it produces).

**Evidence** A named value that a Scenario produces upon successful execution
(e.g., an order ID, a session token, a created resource).

**Precondition** A named value that a Scenario requires in order to execute. A
Scenario cannot run while any of its Preconditions remain unset.

**Execution Plan** A graph describing which Scenarios to execute, in what order,
and how Evidence from one Scenario is wired to the Preconditions of another.
Users can create Execution Plans manually or allow an Execution Agent to
generate them.

**Run** The result of executing an Execution Plan against a specific Environment
at a specific point in time. A Run produces concrete Evidence values, full
tracing (HTTP requests, response bodies, timing), and structured logs for every
Scenario executed. This built-in observability is a key advantage over
traditional test scripts, which require significant custom code to achieve
comparable logging and tracing visibility. Runs are immutable historical
records.

Execution Plans are versioned and immutable. When an Agent changes an Execution
Plan, it creates a new version rather than modifying the existing version. A Run
references the exact version of the Execution Plan that was executed and remains
immutable as a historical record. This ensures that historical Runs remain
accurate and reproducible even as Plans evolve.

**Agent** An AI worker that autonomously performs work on the Scenario Knowledge
Base, such as discovering Scenarios or creating and executing Execution Plans.

**Application** A software product or system owned by a team and tested as a
whole. An Application may consist of one or more Components, including a single
monolith or multiple microservices.

**Component** A deployable or logically distinct part of an Application that is
associated with source code, such as a microservice, web application, mobile
application, or monolith.

**Default Component** The initial Component automatically created for an
Application, allowing users to start using AutoAssure without having to define
their application architecture first. Additional Components can be added later.

**Component Identifier** A short, unique identifier for a Component used to make
Components easier to recognize, search, filter, and reference throughout the
AutoAssure interface. AutoAssure could generate one automatically, and users
could change it if needed.

**Environment** A named configuration (e.g., Production, Staging, Pre-Staging)
that holds a set of key-value pairs accessible directly inside Scenarios. Each
Environment is marked as either Production or Non-Production. Scenarios can be
flagged so they are excluded from Production Environments—for example, a "Delete
Test User" Scenario should never run against production. During execution, if
the Execution Agent determines that a Scenario needs a value not yet defined
(such as a URL endpoint), it can prompt the user to add the key-value pair to
the relevant Environment on the fly.

## How It Works

```txt
ORGANISATION
│
└── APPLICATION
     ├── CONFIGURATION
     │    ├── Environments
     │    └── Agents
     │
     ├── COMPONENTS
     │    ├── Component A
     │    ├── Component B
     │    └── Component C
     │
     ├── SCENARIOS
     │    ├── Preconditions
     │    └── Evidence
     │
     ├── EXECUTION PLANS
     │
     ├── RUNS
     │
     └── ..
```

To get started, users create one or more **Applications** within an
organization. Typically, each team owns one Application per product. Employee
access permissions are scoped to Applications.

Within each Application, users define **Components**—the services or
microservices that make up the system. Each Component links to a code repository
and can be pinned to a specific branch or tag. For monolith applications,
AutoAssure automatically creates a single default Component, so users can start
testing immediately without defining their architecture first.

On first launch, AutoAssure guides users through a setup wizard to create their
initial Application and Components.

Within each Application, **Scenarios** describe the behaviors that need to be
tested. Scenarios are not scoped to a single Component because they capture
system-wide behavior that may span multiple Components. Users can create
Scenarios manually, defining their own test behaviors, or they can leverage a
Discovery Agent to automatically inspect Components and source code to uncover
new Scenarios. This dual approach lets teams start testing immediately with
manual design, then scale their test coverage through automated discovery.

When defining a Scenario, users declare its **Preconditions**—named values the
Scenario needs in order to run—and its **Evidence**—named values the Scenario
produces upon successful execution.

**Environments** provide configuration values that Scenarios can reference at
runtime—things like API endpoints, credentials, or feature flags. Each
Environment is a collection of key-value pairs and is classified as Production
or Non-Production. This classification enables safety guardrails: destructive
Scenarios (e.g., deleting test data) can be excluded from Production
Environments. When the Execution Agent runs a plan and encounters a missing
value, it prompts the user to add the key-value pair to the target Environment,
keeping configuration discoverable and up to date.

**Execution Plans** orchestrate multiple Scenarios together by wiring Evidence
from one Scenario to the Preconditions of another. Users can create Execution
Plans manually, or enable an Execution Agent (via Application Settings) to
generate them automatically.

**Runs** are produced when an Execution Plan is executed against a target
Environment. Each Run captures the concrete Evidence values produced by every
Scenario, along with full tracing and structured logs—HTTP requests, response
bodies, timing, and any errors encountered. Users can browse recent Runs from
the Application overview to inspect results without needing to configure
external logging or tracing infrastructure.

**Agents** are optional helpers that operate on the user's behalf. They can be
enabled or disabled in Application Settings. When enabled, agents can discover
Scenarios, generate Execution Plans, and run them. They are a convenience, not a
requirement—users retain full manual control over all of these activities.

## Application Navigation

```txt
┌──────────────────────────────────────────────────────────────────────────────┐
│  AutoAssure ▾     Checkout Application ▾                 🔍  ?  👤           │
├───────────────┬──────────────────────────────────────────────────────────────┤
│               │                                                              │
│  OVERVIEW     │                         Overview                             │
│               │                                                              │
│  ◉ Overview   │   Checkout Application                                       │
│               │   ──────────────────────────────────────────────────────     │
│  TESTING      │                                                              │
│  Scenarios    │   ┌─────────────────┐  ┌─────────────────┐                   │
│  Plans        │   │  42 Scenarios   │  │  96% Passing    │                   │
│  Runs         │   │  38 Ready       │  │  Last run       │                   │
│               │   └─────────────────┘  └─────────────────┘                   │
│               │                                                              │
│  ───────────  │   Recent Activity                                            │
│               │                                                              │
│  SETTINGS     │   ✓ Discovery Agent found 3 new scenarios                    │
│  Settings     │   ✓ Run #1842 completed                                      │
│               │   ⚠ 2 scenarios need attention                               │
│               │                                                              │
│               │   Recent Runs                                                │
│               │   ┌────────┬──────────┬───────────┬──────────────┐           │
│               │   │ #1842  │ Failed   │ 10:42 AM  │ Plan v12     │           │
│               │   │ #1841  │ Passed   │ 09:15 AM  │ Plan v12     │           │
│               │   └────────┴──────────┴───────────┴──────────────┘           │
│               │                                                              │
└───────────────┴──────────────────────────────────────────────────────────────┘
```

## Decision Logs

**1. Agent as a Secondary Concept**

Agents are an implementation and automation concept rather than the primary
object users interact with. Users primarily work with Scenarios, Inputs,
Outputs, and Execution Plans. Agents operate behind these artifacts and are
responsible for discovering, improving, maintaining, and executing them.

Agents remain visible and accessible so users can understand what AutoAssure is
doing, inspect activity and logs, and configure agent behavior when needed.
However, users do not create or manage agents as part of the primary workflow.

Agents are therefore exposed as a **secondary concept**, likely through Project
Settings or a dedicated Agents area, rather than being the starting point of the
user experience.

## Todos

~~1. Should we introduce the concept of Agents?~~

1. Observability and dashboards - where do they sit?
1. Should we add margin when billing customer the use of tokens? Should we allow
   customer to plugin their own open API key or bedrock.
1. How do we execute tests? Deploy test clients? What platforms should test
   clients be written for?
1. Color theme/black & white?
1. How to structure the app? Pages? Menus? Navigation?

```

```
