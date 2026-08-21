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
| `Platform.Workflow` | done — domain, router, engine, resolver, delegation; **20 tests green** |
| `Platform.Persistence` | done — audit, soft delete, xmin, outbox, sequences; **5 integration tests green** |
| `Platform.Identity` | done — users, roles, permissions, admin screens; **10 tests green** |
| `Platform.Notifications` | not started |
| `Platform.Reporting` | not started |
| `Platform.Messaging` | not started |
| **HR module** | employees + leave, wired to the approval engine; **11 tests green** |
| Other modules | not started — each is now a repeat of the HR pattern |

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

## The database

PostgreSQL **18.6**, installed system-wide and running as a service. One database,
one schema per module.

```bash
./dev.sh status    # is it up?
./dev.sh db        # SQL shell
./dev.sh reset     # drop and recreate (asks first)
```

Local dev credentials: role `meierp`, database `mei_erp`, on `127.0.0.1:5432`. The
password lives in the gitignored `appsettings.Development.json`, never in the repo.
One-time bootstrap on a fresh machine:

```bash
sudo -u postgres psql -c "CREATE ROLE meierp LOGIN PASSWORD '<pw>' CREATEDB;" \
                      -c "CREATE DATABASE mei_erp OWNER meierp;"
```

`CREATEDB` matters: the integration tests each create and drop a throwaway database.
Without it they skip, and **a skipping test suite reports green while asserting
nothing** — the exact failure the previous platform shipped with. Verify by breaking
the password on purpose: the suite must report *skipped*, not passed.

## Persistence rules

`ModuleDbContext` enforces these so no module has to remember them:

- **Audit stamping** on insert and update. `CreatedUtc`/`CreatedBy` are frozen after
  insert — without that, an update carrying a stale entity rewrites history.
- **Soft delete** via a global query filter. `Remove()` becomes a flag update; every
  query excludes deleted rows without a single service opting in.
- **Concurrency via PostgreSQL's own `xmin`.** No token to re-stamp by hand, so no
  way to forget to. A lost update raises `DbUpdateConcurrencyException` rather than
  silently winning — there is a test that races two contexts to prove it.
- **Money is `numeric(18,4)` everywhere**, applied by convention over every decimal
  property. Left to hand-declaration, one property eventually becomes a float and a
  trial balance stops balancing.
- **The outbox table lives on every module context**, so an integration event is
  written in the same transaction as the change that raised it.

`DocumentSequence` locks its counter row with `FOR UPDATE` before reading it.
Read-then-write without the lock is the classic duplicate-number bug: it passes every
test, then two people press Save in the same second and both get PO-26-0042.

## Open decisions

- Concurrent user count at peak is unconfirmed; under ~200 needs no capacity planning.
- WhatsApp as a notification channel is planned as a *provider* behind
  `Platform.Notifications`, not a special case. Build the channel abstraction first.
