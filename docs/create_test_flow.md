## Creating new test flow

### First screen

```txt
┌─────────────────────────────────────────────────────────────┐
│ New test                                                    │
│                                                             │
│ What do you want to verify?                                 │
│                                                             │
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ A user can send a message to another user. The          │ │
│ │ recipient should receive the message.                   │ │
│ │                                                         │ │
│ │ ## Notes                                                │ │
| | Don't create new user in this test                      │ │
│ │                                                         │ │
│ └─────────────────────────────────────────────────────────┘ │
│                                                             │
│  ＋ Add context                                  [ Try ▶ ]  │
│                                                             │
│  ─────────────────────────────────────────────────────────  │
│  AutoAssure will determine how to execute this test.        │
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
- **Test data**: Upload files to be used with this test.
- **Environment**: Explicitly specify what to pick from your environment
  variables to use in this scenario.

Items discussed but decided not to include:

- **Execution guidance**, because they can write that into the test description
  above
- **Existing scenarios**, because this belongs to **Execution Plan**

### Clicking on "Add Context > Preconditions"

When selecting **preconditions**, the following form appear:

```
┌─────────────────────────────────────────────────────┐
│ Preconditions                                       │
│                                                     │
│ This test requires these values:                    │
│                                                     │
│ ┌─────────────────────────────────────────────────┐ │
│ │ Name                                            │ │
│ │ Sender User                                     │ │
| |                                                 | |
│ │ Value Source                                    │ │
│ │ [ From Other Scenario  ▼ ]                      │ │
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
- **Value Source**: How do you expect this value to be provided: **From Other
  Scenario**, **Ask Me At Run Time**, **Specific Value**. (tooltip: _What Value
  Should AutoAssure use?_)
- **Example value**: Only show for the first two. Used when building test plan
  to help quickly connect scenarios together. (tooltip: _Give AutoAssure an
  example of the value you're expecting. This helps it identify compatible
  Scenarios and understand how the value should be used._)

For simplicity, the value is deliberately given as text. User can type json,
string, number, floating point, etc, whatever, the execution agent will
understand and use the correct type at runtime.

If **From Other Scenario** is chosen, when clicking "Try" the user will be
prompted to pick another scenario if it can't automatically work out what
scenario to choose from.

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

Short cut: [Cmd + Enter]; It opens a new panel on the right.

Idea: "I told it WHAT to test, and I can watch it figure out HOW."

```
┌─────────────────────────────────────────────────────────────┐
│ Try test                                      ● Running      │
│                                                             │
│ A user can send a message to another user.                  │
│                                                             │
├─────────────────────────────────────────────────────────────┤
│ Execution                                                   │
│                                                             │
│ ✓ Understanding test                                       │
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
│ Try test                                                     Running │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  A user can send a message to another user.                          │
│  The recipient should receive the message.                           │
│                                                                      │
│  ──────────────────────────────────────────────────────────────────  │
│                                                                      │
│  ✓ Understanding test                                                │
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
✓ Test passed

4 behaviours executed
3 preconditions resolved
1 message created
1 assertion verified

Duration: 4.2s


Execution
  ✓ Create sender
  ✓ Create receiver
  ✓ Send message
  ✓ Verify delivery
```

DON'T TURN THIS INTO A GIANT DEVELOPER CONSOLE.

### Evidence

After clicking "try", AutoAssure runs the scenario, and automatically suggest to
collect evidences:

```txt
"AutoAssure found values that may be useful to other tests".


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
New Test

[description...]

+ Add context
   ├── Preconditions
   ├── Test data
   └── Environment

────────────────────────────────────────

Evidence
AutoAssure will suggest useful outputs after the
test is tried.

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
│ From Other Scenario                              │
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
