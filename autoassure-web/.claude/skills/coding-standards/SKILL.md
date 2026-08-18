---
name: coding-standards
description:
  MUST load this BEFORE writing or editing any .ts or .tsx file in this repo.
---

- Components MUST be `export const Foo = memo(function Foo(props) { ... })`.
- Inline comments MUST explain INTENTION ("when X, do Y"), not mechanics.
- UI MUST use Mantine (`@mantine/core`) components, not raw HTML elements.
- Styling MUST prefer CSS variables (Mantine `--mantine-*`) over hard-coded
  values.
- `/src/ioc` MUST contain DI wiring only (Context + Provider + useService hook).
  Application Hooks/logic MUST go in `/src/hooks`.
- `/src/ioc/AppProviders.tsx` MUST list every singleton service's Provider in
  `appProviders`.
- Class methods MUST use `foo() {}`, not arrow class fields.
- Methods that can throw MUST have a `@throws` JSDoc explaining when, so callers
  can avoid/handle it. Include throws from downstream services/calls, not
  just direct `throw`s.
- Interface/class fields MUST be `readonly` unless mutation is intentional.
- Exported classes, public methods, and public APIs MUST have a super short
  JSDoc: what it does, not how.
- Non-obvious constants MUST have a one-line comment saying what they are.
- Services publishing events MUST use `eventemitter3` (typed `EventEmitter`).
- Event types MUST be fine-grained enough listeners can't pick the wrong one,
  but not so many they're a pain to listen to.
- Code (services, components, hooks, etc.) MUST NOT read `import.meta.env` or
  any other config source directly; config MUST be injected via
  constructor/props (read/validated in `/src/ioc`).
- Service-layer methods that throw on a backend call SHOULD use or extend
  `ServiceError` (`src/models/ServiceError.ts`).
- `try/catch` MUST wrap ONLY the call that can throw, not surrounding code.
