# MEI ERP — working notes

A ground-up rebuild of the MEI platform as **one integrated suite** rather than eight
apps behind a shared login. Read this before exploring the tree.

## Status

**Foundation stage.** The spine is being built before any business module, which is
the whole reason this is a rebuild rather than a refactor. Nothing here is in the
office yet.

| Piece | State |
|---|---|
| `Platform.Kernel` | done — entities, clock, result, module catalog |
| `Platform.Workflow` | domain + router done, **20 tests green**; engine implementation next |
| `Platform.Persistence` | not started |
| `Platform.Identity` | not started |
| `Platform.Notifications` | not started |
| `Platform.Reporting` | not started |
| `Platform.Messaging` | not started |
| Business modules | not started — ported one at a time, after the spine |

**The old app at `/home/pc/vb/acc` stays live in the office until this reaches parity,
module by module.** There is no cutover date and there must not be a big-bang switch.

## Shell environment

Every `dotnet` command needs this:

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
export LD_LIBRARY_PATH="$HOME/.local/finance-erp-dev/pkg/usr/lib/x86_64-linux-gnu"
```

`LD_LIBRARY_PATH` is mandatory — ICU is unpacked locally, not system-installed, and
.NET hard-fails at startup without it.

## What is deliberately different from the old platform

Each of these is a correction to something that cost real time in `/home/pc/vb/acc`.
Do not "simplify" them back.

- **One PostgreSQL database, one schema per module** — not nine MariaDB databases.
  Modules stay isolated inside the database, but a report can join across them, one
  backup covers everything, and one transaction spans a cross-module write. This also
  brings **materialized views**, which MariaDB lacks and which the reporting
  requirement genuinely needs.
- **Foreign keys to shared master data are allowed and expected.** The old no-FK rule
  existed only because the databases were separate. Cross-*business*-module FKs are
  still forbidden — those go through events.
- **`xmin` for concurrency**, so there is no token to re-stamp by hand and no way to
  forget to. The old platform hand-rolled this because MariaDB has no rowversion.
- **Central package management** (`Directory.Packages.props`). The old repo drifted to
  .NET 9 packages on a .NET 10 SDK because every csproj carried its own version.
- **One approval engine**, not nine hand-rolled status enums. See below.
- **One report platform**, not five report services with two modules having none.
- **`Result<T>` for business failures**, exceptions only for bugs. "Supplier has stock,
  cannot delete" is the rule working, not an exceptional condition.
- **Tests from the first commit.** The old platform's Finance module — the most
  consequential code in it — had 12 tests.

## Architecture

```
platform/                          the spine, built before any module
  MeiErp.Platform.Kernel           entities, IClock, Result, ICurrentUser, module catalog (NO deps)
  MeiErp.Platform.Workflow         THE approval engine — every module routes through it
  MeiErp.Platform.Contracts        cross-module DTOs + interfaces (NO EF)
  MeiErp.Platform.Persistence      ModuleDbContext: audit, soft delete, outbox, sequences
  MeiErp.Platform.Identity         users, roles, permissions, module access
  MeiErp.Platform.Messaging        outbox-backed integration events
  MeiErp.Platform.Notifications    email / WhatsApp / in-app, one queue
  MeiErp.Platform.Reporting        catalog + table builder + exports
  MeiErp.Platform.Printing         letterhead, barcode, QR, QuestPDF
  MeiErp.Platform.Web              shell, unified nav, global search, dashboard, admin

modules/                           business modules, ported one at a time
host/MeiErp.Host                   the one process that composes everything
tests/
```

**A module depends on `platform/`, never on another module.** That rule is what keeps
this comprehensible; the old platform kept it by accident (via separate databases) and
this one keeps it on purpose.

## The approval engine — read before touching workflow

This is the centrepiece and the reason for the rebuild. The old platform hand-coded
**nine** approval flows — leave, payment requests, advances, payroll runs, quotations,
purchase orders, sales orders, stock transfers, gate passes — each with its own status
enum and transition code, none supporting amount-based routing, delegation, SLA
escalation, or return-for-correction.

- **A definition is versioned and never edited in place** (`Revision`). A request in
  flight keeps running the revision it started on, so changing rules today cannot
  re-route something submitted last week.
- **The step plan is snapshotted onto the request** at submission (`ApprovalStepState`),
  for the same reason.
- **A document matching no step is refused, never auto-approved.** Silent auto-approval
  is the worst failure mode an approval engine has, because it looks like success.
  There is a test for exactly this.
- **A step with an amount band does not apply to a document with no amount** — it
  drops out rather than guessing.
- **Return is not rejection.** Reject is terminal; return sends the document back to
  the raiser alive, keeping its history. Resubmit restarts routing from step one,
  because a corrected document is a different document.
- **`ApprovalAction` is append-only.** Correcting a mistake means another action, never
  an edit. This is the only defence when an approval is disputed a year later.
- **Segregation of duties is data, not convention** — `BlockSelfApproval` defaults on,
  `RequireDistinctApprovers` is available.
- **A delegated approval records who it was on behalf of.** One that looks identical to
  a direct approval is how accountability gets lost.
- **`WorkflowRouter` is pure** — no database, no clock, every date passed in. That is
  what makes it testable, and it is why the old platform's equivalent was not.
- **Modules keep their own status enums.** The engine drives them through
  `IApprovalSink`. That is what lets the nine flows migrate **one at a time**, each
  independently reversible. Migrating them together is the fastest way to break
  production.

Migration order when the modules land: **leave requests first** — highest volume,
simplest, nothing financial at risk. Prove the engine there before anything that moves
money.

## Conventions

- **Never read the clock directly.** Inject `IClock`. `Today` is the *business* date;
  `UtcNow` is for timestamps. Entities take a `DateOnly today` parameter rather than
  reading it, which is what makes date-boundary behaviour testable with `FixedClock`.
- **Some warnings are errors** (`Directory.Build.props`): `RZ10012`, `BL0008`, `CS4014`
  and the nullability set. RZ10012 means a component rendered as an unknown HTML
  element — the page returns 200 with a section silently missing. It happened twice on
  the old platform.
- **Secrets never go in `appsettings.json`.** Dev config is gitignored; production comes
  from environment variables.
- **Business failures return `Result`; exceptions are for bugs.**
- **Every page, nav item and action is permission-gated.**

## Commands

```bash
dotnet build                                   # whole solution
dotnet test                                    # everything
dotnet test tests/MeiErp.Platform.Workflow.Tests
```

## Open decisions

- **PostgreSQL is not installed yet.** `sudo apt install postgresql` (v18 available).
  The provider choice is still cheap to reverse — nothing is wired to Npgsql yet.
- Concurrent user count at peak is unconfirmed; under ~200 needs no capacity planning.
- WhatsApp as a notification channel is planned as a *provider* behind
  `Platform.Notifications`, not a special case. Build the channel abstraction first.
