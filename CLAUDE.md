# MEI ERP — working notes

A ground-up rebuild of the MEI platform as **one integrated suite** rather than eight
apps behind a shared login. Read this before exploring the tree.

## Status

**All seven modules are in and the app runs**, with 116 tests green and no build
warnings. Nothing is in the office yet — the old app stays live until this reaches
parity feature by feature.

What is deliberately *not* built yet: the outbox event bus that would let
Inventory and Repair post to Finance's ledger automatically, and the scheduler
behind the approval engine's SLA reminders. Those are the next pieces, and the
sections below describe how they are meant to fit. `HANDOVER.md` is the honest
account of how far short of the old app this still is.

| Piece | State |
|---|---|
| `Platform.Kernel` | done — entities, clock, result, module catalog |
| `Platform.Workflow` | done — domain, router, engine, resolver, delegation; **20 tests green** |
| `Platform.Persistence` | done — audit, soft delete, xmin, outbox, sequences; **5 integration tests green** |
| `Platform.Identity` | done — users, roles, permissions, admin screens; **10 tests green** |
| `Platform.Notifications` | done — channels, durable queue, preferences, bell; **33 tests green** |
| `Platform.Reporting` | not started |
| `Platform.Messaging` | not started |
| **HR module** | employees + leave, wired to the approval engine; **11 tests green** |
| **Finance module** | chart of accounts, vouchers, payment requests, reports; **18 tests green** |
| **Inventory module** | items, stock, purchasing, sales; **19 tests green** |
| **Fleet module** | vehicles, servicing, running costs, expiry reminders |
| **Gate Pass module** | inward/outward passes, returns; **12 tests green** |
| **Repair module** | jobs as a state machine, work items, delivery; **9 tests green** |
| **Tender & Projects** | bids, guarantees, projects, task board; **12 tests green** |

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

## Finance — the rules that must not be relaxed

- **Everything posts to the ledger.** No module writes financial state directly;
  they call `PostSystemVoucherAsync`. That is the guarantee the books balance.
- **An unbalanced entry is refused**, including one handed over by another module.
  One bad caller must not be able to corrupt the ledger.
- **A posted voucher is immutable.** The correction path is reverse (an equal and
  opposite entry, original untouched) or duplicate-as-draft. Editing a posted entry
  makes every report printed before it a lie.
- **Only leaves can be posted to.** A heading with its own balance double-counts
  itself against its children in every report.
- **Nothing posts into a closed year**, so a signed-off trial balance stays signed off.
- **An account with entries can never be deleted** — only deactivated. This is what
  stops Account's soft-delete filter hiding voucher lines and silently unbalancing
  the trial balance; there is a test pinning it, and a note in `FinanceDbContext`.
- **The balance sheet carries profit-not-yet-closed as its own line.** Without it the
  sheet fails to balance for the whole of every year until year-end.
- **A ledger row names the contra head, not its own.** "Cash" on every row of the cash
  ledger tells the reader nothing; a multi-line voucher reads "Split — N heads"
  rather than inventing a single head.
- **Approval authorises spend; it does not move money.** The voucher is posted when
  someone actually pays, which is what lets an approved request wait for funds
  without the books claiming it was settled.

## Inventory — the rules that must not be relaxed

- **`StockService` is the only thing that moves stock.** Every change writes a
  `StockMovement` and updates the running quantity together. A figure changeable
  from two places is a figure nobody can trust; the edit screen cannot touch it,
  and there is a test proving an edit that tries is ignored.
- **The movement history is append-only and is the truth.** `Item.QuantityOnHand`
  is a cache; `RebuildQuantitiesAsync` recomputes it from movements when it drifts.
- **The average is weighted by quantity, not by price.** 90 at 100 plus 10 at 200
  is 110, not 150 — a naive mean overstates stock value by a third.
- **Issuing does not move the average.** Only purchases change what stock is carried at.
- **A delivery snapshots the cost it went out at.** Reading the average live would
  silently rewrite last month's margin every time somebody bought at a new price.
- **Stock never goes negative** — it is a lie that surfaces during a count.
- **An adjustment needs a reason.** An unexplained one is indistinguishable from theft.
- **Confirming a sales order reserves nothing.** A soft reservation the stock figure
  does not honour is worse than none: two orders can still be promised the same unit
  while both look safe. Short lines are backorders, caught at delivery.
- **A delivery checks every line before moving any of it**, so a refusal leaves no
  half-posted note to unpick by hand.
- **Goods cannot be received against an unapproved order**, or someone commits the
  company to a purchase by unloading a van.
- **An item with stock or history can never be deleted** — only deactivated. Same
  reason as Finance's accounts: it keeps the soft-delete filter from hiding movements.

## Notifications — the rules that must not be relaxed

The approval engine's best features were invisible until this existed: somebody
raised a request and their manager only found out if they happened to open the
inbox.

- **A notification is staged, never sent inline.** `NotifyAsync` and
  `DismissEventAsync` write rows and return; **the caller commits.** The tables
  live on `PlatformDbContext` precisely so a notification lands in the same
  transaction as the approval that raised it. An approval that commits while its
  notification rolls back leaves somebody waiting on a queue nobody told them
  about; the reverse tells them about something that never happened. Sending
  inline would also let a slow SMTP server hold open the transaction that
  approves a payment.
- **The bell's own actions do save** — `MarkReadAsync`, `MarkAllReadAsync`.
  Nothing else is in flight when a person clicks one.
- **Every channel gets a row, including the ones that did nothing.** Suppressed
  by preference and unreachable for want of an address are recorded as
  `Suppressed` and `NotApplicable`, not omitted. "We never tried" and "we tried
  and it bounced" are different answers to the only question anyone asks
  afterwards, and a missing row cannot tell them apart.
- **No address is not a failure.** It is a fact about the account, so it is
  never retried — otherwise every attempt burns against something that cannot
  change without somebody editing the user.
- **The attempt is counted when the row is claimed, not when the send returns.**
  `ClaimDueAsync` increments `Attempts` and clears `NextAttemptUtc` in the same
  UPDATE that selects the rows, under `FOR UPDATE SKIP LOCKED`. That is what
  stops two app instances sending the same email, and what stops a message that
  hangs the dispatcher being retried forever.
- **A dead delivery carries no next-attempt time.** A dispatcher that filters
  only on the clock would otherwise pick it up for ever.
- **A channel that throws is a bug in that channel**, caught and turned into a
  failed attempt so it cannot take the rest of the batch down.
- **Categories are constants, not free strings** (`NotificationCategories`). A
  typo would create a second category nobody has a preference for, which then
  quietly ignores their opt-out.
- **Email is off by default for most categories.** Emailing every status change
  teaches people to filter the system into a folder they never open, and the one
  message that mattered is lost with the rest.
- **Deciding a step stands down everyone else holding it.** A bell full of
  things already handled trains people to ignore the bell. Notifications the
  person has already *read* are left alone — dismissing those would rewrite what
  they saw.

`RetrySchedule` is pure and static for the same reason `WorkflowRouter` is: the
awkward cases are all about time, and none are testable if the rule reads the
clock itself.

## Gate Pass — why it exists

The module is a segregation-of-duties control, not a form. **Whoever raises a pass
cannot clear it through the gate** — enforced in `GatePassService.ClearAsync`, not
in the UI, because the UI is the easy half to bypass. A cleared pass also becomes
uneditable and uncancellable: security is holding a printed copy, and a record that
can still change proves nothing about what actually left. Returnable passes stay
open until the last item is ticked back, since partial returns are the normal case.

## The tracked-entity trap — this has bitten twice

An edit screen loads an entity through a service and hands **that same instance**
back to save. EF is tracking it, so inside the service `existing` and the incoming
object are the *same reference*:

```csharp
var existing = await db.X.FirstOrDefaultAsync(...);   // same object as `input`
if (existing.Side != input.Side) { ... }              // ALWAYS false
input.Quantity = existing.Quantity;                   // preserves nothing
```

Both a guard and a field-preserving assignment written this way silently do
nothing, and no test catches it unless the test goes through the same load-then-save
path a real screen does. It got past review twice — once on `Item.QuantityOnHand`,
once on `ThirdParty.Side`.

**Read the previous value from the change tracker instead:**

```csharp
var before = db.Entry(existing).OriginalValues.GetValue<T>(nameof(X.Field));
```

That works whether or not the caller handed back the tracked instance.

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
- WhatsApp as a notification channel is a *provider* behind
  `Platform.Notifications`, not a special case. The abstraction is built:
  implement `INotificationChannel`, register it, and choose what
  `EnabledByDefault` returns. Nothing else changes, and no call site moves.
