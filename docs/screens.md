## Screens (WIP)

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
