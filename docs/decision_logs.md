## Decision Logs

### 1. Agent as a Secondary Concept

Agents are an implementation and automation concept rather than the primary
object users interact with. Users primarily work with **Scenarios and
Activities**. Agents operate behind these artifacts and are responsible for
discovering, improving, maintaining, and executing them.

Agents remain visible and accessible so users can understand what AutoAssure is
doing, inspect activity and logs, and configure agent behavior when needed.
However, users do not create or manage agents as part of the primary workflow.

Agents are therefore exposed as a secondary concept, likely through Project
Settings or a dedicated Agents area, rather than being the starting point of the
user experience.

### 2. ~~Execution Plans are Natural-Language Documents~~ (Superseded)

> **Superseded.** The Execution Plan / Test Plan concept has been removed
> entirely. Scenario is now the primary testing entity, made up of one or more
> Activities. There is no separate multi-Scenario document to author — you Try
> or Run one or more Scenarios directly, and AutoAssure determines execution
> order, dependency resolution, and whether a failed Activity should prevent
> later Activities from running. Explicit Scenario dependencies still exist,
> but only as an advanced escape hatch (`Once` / `Fresh`), not as the normal
> way of composing tests.

Execution Plans were originally authored as natural-language documents rather
than graphs or test scripts, with AutoAssure interpreting them and building the
execution graph automatically. This supported the same underlying goal that
Scenarios/Activities now serve directly: letting engineers describe **what**
they want verified without having to manage **how** it is executed.

### 3. Rust, Open Source, Cross-Platform Test Client

The AutoAssure **Test Client** will be implemented in **Rust** and released as
open-source software.

The Test Client will be distributed as a small, self-contained native binary
with no requirement for users to install a language runtime such as Node.js,
Python, or the JVM.

Initial supported platforms will be:

- Linux x86-64
- Linux ARM64
- Windows x86-64
- macOS Intel x86-64
- macOS Apple Silicon ARM64

Rust provides official support for the major Linux, Windows, and macOS targets,
including ARM64 macOS.

### Why Rust

The Test Client is infrastructure rather than an end-user application. It should
be:

- small and easy to distribute
- fast to start
- predictable in production and CI/CD environments
- independent of external language runtimes
- easy to deploy as a single binary
- suitable for long-running daemon deployments
- suitable for ephemeral CI/CD execution

Using Rust avoids requiring customers to install and maintain additional
runtimes or package dependencies simply to connect AutoAssure to their
environment.

This also gives the Test Client a clear operational boundary: customers install
one binary, configure its permissions, and run it.

### Why Open Source

The Test Client runs inside customer infrastructure and may have access to
internal systems. Customers should be able to inspect exactly what software they
are installing and running.

Open sourcing the Test Client provides transparency and increases trust without
requiring the AutoAssure Cloud or its core product capabilities to be open
source.

The open-source Test Client is therefore treated as an infrastructure and trust
component, while AutoAssure Cloud remains the primary commercial product.

### Security and Permissions

The Test Client will enforce a local permission policy before executing
requests.

Permissions may restrict capabilities such as:

- target domains
- network destinations
- execution types
- other capabilities as the product evolves

The permission layer belongs to the Test Client rather than the Execution Agent
so that local policy cannot be bypassed by an Execution Agent.

The default configuration should optimize for ease of adoption, while
documentation and configuration options should make it straightforward for
organizations to apply stricter restrictions.

### Cross-Compilation and Release Builds

Developers do not need a separate physical machine for every supported platform.

Rust supports compilation to multiple target triples, and `rustup` can install
the required target toolchains.

The project should use **CI/CD to produce release binaries** for the supported
platforms. GitHub Actions provides hosted Linux, Windows, and macOS runners,
including both Intel and ARM64 variants.

A typical release pipeline will therefore build:

```text
Linux x86-64      → x86_64-unknown-linux-gnu
Linux ARM64       → aarch64-unknown-linux-gnu
Windows x86-64     → x86_64-pc-windows-msvc
macOS Intel        → x86_64-apple-darwin
macOS Apple Silicon→ aarch64-apple-darwin
```

For example, a Rust target can be installed and built with:

```bash
rustup target add aarch64-apple-darwin
cargo build --release --target aarch64-apple-darwin
```

The exact build strategy may use native CI runners rather than cross-compiling
every target from one machine. This is preferable for release builds because it
also allows each platform's binary to be tested in its native environment.

GitHub Actions can run separate Linux, Windows, and macOS jobs and then publish
the resulting binaries as one AutoAssure Test Client release.

### Rationale

The goal is not to make Rust part of the user experience. The goal is to make
the Test Client **disappear** from the user's operational complexity.

A customer should be able to download:

```text
autoassure-client
```

run it, configure its permissions, and connect it to AutoAssure without first
installing a programming language runtime or dependency manager.

Rust and open source together provide a strong combination of **deployment
simplicity, performance, portability, and customer trust**.

### Consequence

The Test Client becomes a stable, independently distributed infrastructure
component. Its protocol and security boundary should therefore be designed
carefully so that Execution Agents can evolve independently without requiring
frequent changes to the customer's installed Test Client.
