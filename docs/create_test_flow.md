## Creating new Scenario flow

### First screen

`Try` belongs to the **Scenario**, not to an individual Activity — a Scenario
is what you Try or Run. A Scenario can (and usually will) contain multiple
Activities; you don't have to declare them as separate fields up front. Write
what you want verified as plain text, and AutoAssure structures it into
Activities for you (visible after you Try).

```txt
┌─────────────────────────────────────────────────────────────┐
│ New Scenario                                                 │
│                                                             │
│ What do you want to verify?                                 │
│                                                             │
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ A user can send a message to another user. The          │ │
│ │ recipient should receive the message.                   │ │
│ │                                                         │ │
│ │ ## Notes                                                │ │
| | Don't create new user in this scenario                  │ │
│ │                                                         │ │
│ └─────────────────────────────────────────────────────────┘ │
│                                                             │
│  ＋ Add context                                  [ Try ▶ ]  │
│                                                             │
│  ─────────────────────────────────────────────────────────  │
│  AutoAssure will determine how to execute this Scenario,    │
│  including how many Activities it breaks down into.         │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### Clicking on "Add context"

```
Add context (drop down)

○ Preconditions
○ Test data
○ Environment
```

In which when hover, it displays the following tooltip/description

- **Preconditions**: Named values that this scenario requires.
- **Test data**: Upload files to be used with this Scenario.
- **Environment**: Explicitly specify what to pick from your environment
  variables to use in this scenario.

Items discussed but decided not to include:

- **Execution guidance**, because they can write that into the Scenario
  description above
- **Existing scenarios**, because explicit Scenario dependencies are an
  advanced escape hatch, not something to configure from this screen. Normally
  AutoAssure determines how to satisfy a Scenario's requirements itself.

### Clicking on "Add Context > Preconditions"

When selecting **preconditions**, the following form appear:

```
┌─────────────────────────────────────────────────────┐
│ Preconditions                                       │
│                                                     │
│ This Scenario requires these values:                │
│                                                     │
│ ┌─────────────────────────────────────────────────┐ │
│ │ Name                                            │ │
│ │ Sender User                                     │ │
| |                                                 | |
│ │ Value Source                                    │ │
│ │ [ From Prior Activities  ▼ ]                    │ │
│ │                                                 │ │
│ │ Example Value                                   │ │
│ │ alice@example.com                               │ │
│ └─────────────────────────────────────────────────┘ │
│                                                     │
│ + Add more context                                  │
└─────────────────────────────────────────────────────┘
```

In which:

- **Name**: free text field. It should be descriptive so the agent know how to
  apply this value. (tooltip: _Give this value a meaningful name that describes
  what it represents. Examples: Sender User, Order Number, Authentication Token,
  Customer Name, Customer Id_)
- **Value Source**: How do you expect this value to be provided: **From Prior
  Activities**, **Ask Me At Run Time**, **Specific Value**. (tooltip: _What
  Value Should AutoAssure use?_)
- **Example value**: Only show for the first two. Used to help AutoAssure
  identify a matching Precondition or Activity output. (tooltip: _Give
  AutoAssure an example of the value you're expecting. This helps it identify
  compatible Activities and understand how the value should be used._)

For simplicity, the value is deliberately given as text. User can type json,
string, number, floating point, etc, whatever, the execution agent will
understand and use the correct type at runtime.

If **From Prior Activities** is chosen, when clicking "Try" the user will be
prompted to pick which prior Activity to use if it can't automatically work out
which one to choose from.

> **Note:** You shouldn't normally need to add explicit Preconditions.
> AutoAssure first tries to discover or acquire the values an Activity needs on
> its own. Add a Precondition only when AutoAssure can't infer what's required —
> and prefer picking an existing entry from the Precondition library (e.g.
> `Authenticated User`) over typing a new one.

### Clicking on "Add Context > Test Data"

Shows an upload form to allow user to select files.

```
┌─────────────────────────────────────────────────────┐
│ Preconditions                                       │
│                                                     │
│ ...                                                 │
│                                                     │
│ Files                                               │
|                                                     |
│ ┌─────────────────────────────────────────────────┐ │
| |              Drop files here to upload          | |
│ └─────────────────────────────────────────────────┘ │
│                                                     │
│ + Add more context                                  │
└─────────────────────────────────────────────────────┘
```

### Clicking on "Add Context > Environment"

Show a section to allow user to specify environments.

```
┌─────────────────────────────────────────────────────┐
│ Preconditions                                       │
│ Files                                               │
│ Environments                                        │
|                                                     |
| Name                                                |
| SMTP Server                                         |
|                                                     |
| Value                                               |
| [ SMTP_SERVER_ADDRESS ▼ ]                           |
|                                                     |
| Instruction                                         |
| Use this email server to send email.                |
│                                                     │
│ + Add more context                                  │
└─────────────────────────────────────────────────────┘
```

### Clicking on Try

Short cut: [Cmd + Enter]; It opens a new panel on the right. `Try` acts on the
whole **Scenario** — there is no separate "Try" per Activity. The panel below
shows AutoAssure breaking the Scenario down into Activities (Sending message,
Verify recipient received message, …) and executing them top-to-bottom.

Idea: "I told it WHAT to verify, and I can watch it figure out HOW."

```
┌─────────────────────────────────────────────────────────────┐
│ Try Scenario                                   ● Running      │
│                                                             │
│ A user can send a message to another user.                  │
│                                                             │
├─────────────────────────────────────────────────────────────┤
│ Activities                                                   │
│                                                             │
│ ✓ Understanding scenario                                    │
│   Identified required behaviour                             │
│                                                             │
│ ✓ Resolving preconditions                                   │
│   senderUser                                                │
│   receiverUser                                              │
│                                                             │
│   Finding how to obtain senderUser...                       │
│   → Create Test User                                        │
│                                                             │
│   Finding how to obtain receiverUser...                     │
│   → Create Test User                                        │
│                                                             │
│ ▶ Sending message                                           │
│   → Send Message                                            │
│                                                             │
│ ○ Verify recipient received message                         │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

The panel is interactive, like claude code, for example:

```txt
┌──────────────────────────────────────────────────────────────────────┐
│ Try Scenario                                                 Running │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  A user can send a message to another user.                          │
│  The recipient should receive the message.                           │
│                                                                      │
│  ──────────────────────────────────────────────────────────────────  │
│                                                                      │
│  ✓ Understanding scenario                                            │
│    Requires: senderUser, receiverUser                                │
│                                                                      │
│  ✓ Resolving senderUser                                              │
│    → Found "Create Test User"                                        │
│    → Evidence: User                                                  │
│                                                                      │
│  ⚠ Resolving receiverUser                                            │
│                                                                      │
│    I found multiple ways to provide a User:                          │
│                                                                      │
│    ┌──────────────────────────────────────────────────────────────┐  │
│    │  1  Create Test User                                         │  │
│    │     Produces: User                                           │  │
│    │                                                              │  │
│    │  2  Create Customer                                          │  │
│    │     Produces: User                                           │  │
│    │                                                              │  │
│    │  3  Create Admin User                                        │  │
│    │     Produces: User                                           │  │
│    └──────────────────────────────────────────────────────────────┘  │
│                                                                      │
│    Which should I use?                                               │
│                                                                      │
│    > 1                                                               │
│                                                                      │
│    [Enter] Select     [Esc] Cancel                                   │
│                                                                      │
└──────────────────────────────────────────────────────────────────────┘
```

After complete, it shows a nice report:

```txt
✓ Scenario passed

4 activities executed
3 preconditions resolved
1 message created

Duration: 4.2s


Activities
  ✓ Create sender
  ✓ Create receiver
  ✓ Send message
  ✓ Verify delivery
```

DON'T TURN THIS INTO A GIANT DEVELOPER CONSOLE.

### Evidence

After clicking "try", AutoAssure runs the Scenario, and automatically suggest to
collect evidences:

```txt
"AutoAssure found values that may be useful to other Activities and Scenarios".


☑ message
☑ messageId
☐ messageBody
☐ deliveryStatus

[ Add selected evidence ]
```

Notes: DO NOT automatically make every observed value Evidence. Evidence should
represent meaningful outputs of the behaviour, not everything AutoAssure
happened to observe. So the agent should suggest semantically useful outputs.

User can also manually add evidence:

```txt
New Scenario

[description...]

+ Add context
   ├── Preconditions
   ├── Test data
   └── Environment

────────────────────────────────────────

Evidence
AutoAssure will suggest useful outputs after the
Scenario is tried.

[ + Add evidence ]
```

### Saving the Scenario

Add a Save button next to "Try" button.

```txt
                         [ Save ]   [ Try ▶ ]
                         Saved automatically · 2 sec ago
```

### Appendix

Don't make all contexts appear as one giant form.

```
┌──────────────────────────────────────────────────┐
│ Preconditions                                    │
│                                                  │
│ Sender User                                      │
│ From Prior Activities                            │
│ Example: alice@example.com                       │
│                                                  │
│ Receiver User                                    │
│ Ask Me At Run Time                               │
└──────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────┐
│ Test data                                        │
│                                                  │
│ 📎 invoice.pdf                                   │
│ 📎 customers.csv                                 │
└──────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────┐
│ Environment                                      │
│                                                  │
│ SMTP Server → SMTP_SERVER_ADDRESS                │
└──────────────────────────────────────────────────┘

+ Add context
```

---

Questions to discuss:

2. Where does evidence live?
