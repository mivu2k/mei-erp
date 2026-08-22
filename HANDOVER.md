# MEI ERP rebuild — handover

Written 22 August 2026, at the end of the first build session. Read this before
`CLAUDE.md`; that file explains *how* the code works, this one explains **where
the project actually stands and what to do next**.

---

## The honest summary

**This rebuild is at roughly 55% of the old app, and Finance is the only module
anywhere near complete.** Everything else is a working skeleton with the main
screens and none of the depth.

| | Old app (`/home/pc/vb/acc`) | This rebuild | |
|---|---|---|---|
| Code (excl. migrations) | 57,065 | 31,169 | **55%** |
| Pages | 175 | 96 | 55% |
| Tests | 226 | 227 | **101%** |
| Modules | 8 | 7 | Plain Ledger missing entirely |

The old app also has whole subsystems this one has never touched — attendance,
the physical file registry, warehouses, quotations. Those are listed below.

**The old app at `/home/pc/vb/acc` must stay live in the office.** Nothing here
is ready to replace it, and there is no cutover plan. This rebuild is not usable
as a business system yet.

---

## What genuinely works

Verified running, with tests, not just compiling.

### Platform (the reason the rebuild was worth starting)

- **Approval engine** — one engine, amount-band routing, delegation, SLA fields,
  return-for-correction, segregation of duties. Replaces nine hand-coded flows
  in the old app. `WorkflowRouter` is pure and has 20 tests.
- **Approvals inbox** — one queue across every module.
- **Workflow designer** at `/admin/workflows` — levels and amount bands are
  configuration, not code.
- **Users, roles, departments** — permission matrix, reporting lines, last-admin
  protection, cycle detection in the reporting line.
- **Report platform** — one shape per report; screen, Excel and PDF all render
  from it so they cannot disagree. Six Finance reports registered.
- **Printing** — QuestPDF documents in A4 / 80mm / 62mm, Code 128 and QR that
  round-trip through a real decoder, real Excel workbooks with numbers as
  numbers.
- **Persistence** — PostgreSQL, one database, schema per module, audit stamping,
  soft delete, `xmin` concurrency, outbox table (unused so far).

### Finance — complete against the old app

Chart of accounts · vouchers with reversal · day book · payment requests ·
advances with the full disburse → justify → settle flow · petty cash · utilities ·
third parties with statements · payroll with pro-rating and advance recovery ·
bank reconciliation · year close · 6 reports · PDF and Excel export.

**78 tests.** This is the one module I would defend as production-shaped.

### The other six modules — skeletons

| Module | What exists | What is missing |
|---|---|---|
| HR | Employees, leave with balances and approval | **All of attendance** (see below) |
| Inventory | Items, stock ledger, PO→receipt, SO→delivery, parties | Warehouses, transfers, stock counts, product→model→accessory, serials, reports |
| Repair | Jobs as a state machine, work items, delivery | Intakes, quotations, sales orders, payments, parts, purchasing, tracking board, scan, **11 printed documents**, **15 reports** |
| Fleet | Vehicles, servicing, running costs, expiry alerts | Roughly at parity — the smallest module |
| Gate Pass | Passes, partial returns, segregation of duties | Demo goods issuance |
| Tender | Tenders, guarantees, projects, task board | **The physical file registry** — files, movements, stickers, scan — plus milestones |

---

## What is missing — ordered by how much it matters

### 1. Notifications — built; three pieces still open

`Platform.Notifications` exists and the approval engine is wired to it, so
raising a request now tells the people who can approve it, and settling one
tells the raiser. In-app and email channels, a durable queue with exponential
backoff, and per-user per-category preferences. 33 tests, 8 of them against a
real database because the claim statement is hand-written SQL.

What is still missing:

- **A preferences screen.** The `NotificationPreferences` table is read on every
  send and nothing writes to it, so a person cannot yet turn a channel off.
- **A dead-letter screen.** `INotificationOutbox.DeadAsync` and `RetryAsync`
  exist and nothing calls them, so a message that gave up is invisible.
- **Templates.** Email is plain text; it should render Razor on the company
  letterhead like the PDFs do.

The SLA fields on `WorkflowStep` (`ReminderAfterHours`, `EscalateAfterHours`) are
still **stored but never acted on** — there is no background job reading them.
The `approval.reminder` and `approval.escalated` categories are declared and
nothing raises them. That needs a scheduler, and is item 3 below.

### 2. Attendance — the whole subsystem, ~2,000 lines in the old app

Old app has: attendance stations, a kiosk page, NFC card reading, a rotating
HMAC QR code per employee, punch derivation (first punch in, last punch out),
manual corrections that survive re-sync, approved leave outranking punches,
monthly register, recompute. **None of it exists here.** HR leave works; HR
attendance does not exist at all.

This is used every day by every member of staff in the old system. It is the
single biggest functional hole.

### 3. Plain Ledger module — does not exist

The old app's eighth module: main ledgers, sub-ledgers, unlimited nesting,
paired transfers, its own head tree. Entirely absent here. Read the old
`CLAUDE.md` section on it — the design decisions there are good and worth
copying rather than re-deriving.

### 4. The outbox / cross-module posting

`OutboxMessage` exists as a table and nothing writes to it. Inventory and Repair
do not post to Finance's ledger — a goods receipt moves stock but raises no
payable, a repair invoice raises no receivable. **This was the whole argument for
the rebuild** and it has not been built.

Needs: `Platform.Messaging` with a dispatcher, `IPostingGateway` implemented by
Finance, a posting-rules table, and a dead-letter review screen. Build the
dead-letter screen *with* the bus, not after.

### 5. Module depth

Per the table above. Repair and Inventory are the furthest behind. Repair in
particular is missing its entire commercial half (quotations → orders →
payments) and all of its printing.

### 6. Per-record printed documents

The print platform exists and works, but **no module uses it yet**. Only reports
export. The old app prints 11 repair documents, invoices, delivery notes, gate
passes, labels and stickers. Every one of those is unbuilt.

### 7. Platform gaps

- **Global search** — nothing.
- **Audit trail viewer** — audit columns are stamped, nothing reads them.
- **Password reset by email, 2FA, email confirmation** — none.
- **Dashboard** is static placeholder tiles showing zeros.
- **Backups, staging environment, monitoring** — none of the infrastructure
  work from the original plan.

---

## Known bugs and rough edges

- The dashboard's four tiles are hardcoded zeros.
- Reports have no scheduling, saved views, or drill-through targets wired up
  (`DrillUrl` is populated on two reports; nothing else uses it).
- No module registers reports except Finance.
- `PayrollEmployee` duplicates HR's `Employee`. Deliberate for now (payroll must
  work without HR installed) but they will need reconciling.
- Attendance-driven pro-rating in payroll takes a `daysWorked` dictionary that
  **nothing currently supplies** — it defaults to a full month for everyone.

---

## Where to pick up

In order. Each is a session's work or less unless noted.

1. ~~**Build `/admin/company`**~~ — **done.** The page exists at
   `/admin/company`; two bugs under it were fixed on the way (`SaveAsync`
   clobbered the primary key, and `GetAsync` handed the process-wide cache to
   the caller). Six tests.
2. ~~**Notifications and email**~~ — **mostly done.** `Platform.Notifications`
   exists: channel abstraction (in-app + email over MailKit), a durable queue
   with backoff and a dead-letter state, per-user per-category preferences, the
   bell in the app bar, and the approval engine wired to raise on assignment and
   on settlement. 33 tests. **Still missing:** a preferences screen, a
   dead-letter review screen, and Razor/letterhead templates — email is plain
   text today.
3. **Scheduler + SLA reminders and escalation** — makes the stored SLA fields
   real. The `approval.reminder` and `approval.escalated` categories already
   exist and nothing raises them; that is the gap.
4. **Attendance** (2–3 sessions) — the biggest daily-use hole.
5. **Plain Ledger module** (1–2 sessions) — self-contained, copy the old design.
6. **Outbox and cross-module posting** (2 sessions) — the rebuild's actual thesis.
7. **Per-record printing** across modules (2 sessions).
8. **Repair depth** — quotations, orders, payments, parts (3 sessions).
9. **Inventory depth** — warehouses, transfers, counts, serials (2 sessions).

Realistically **12–18 more sessions to parity**, and parity is the floor, not the
goal.

---

## How to run it

```bash
cd /home/pc/vb/mei-erp
./dev.sh up          # http://localhost:5090
./dev.sh test
./dev.sh db          # psql shell
```

Sign in: `admin@mei.local` / `ChangeMe!2026` (forces a password change).

Every `dotnet` command needs:

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
export LD_LIBRARY_PATH="$HOME/.local/finance-erp-dev/pkg/usr/lib/x86_64-linux-gnu"
```

PostgreSQL 18, database `mei_erp`, role `meierp`. Eight schemas: `platform`,
`finance`, `hr`, `inventory`, `repair`, `auto`, `gatepass`, `tender`.

---

## Rules that must not be relaxed

Fully documented in `CLAUDE.md`. The short version:

- Everything financial posts through `PostSystemVoucherAsync`. No module writes
  a balance directly.
- Posted vouchers are immutable — reverse, never edit.
- Nothing posts into a closed fiscal year.
- An account or item with history is deactivated, never deleted.
- A document matching no approval step is **refused**, never auto-approved.
- `WorkflowRouter` stays pure — no database, no clock.
- Never read the clock directly; inject `IClock`.
- A test that cannot run must fail, not pass.

### The trap that caught me twice

An edit screen loads an entity through a service and hands **the same tracked
instance** back to save, so `existing` and the incoming object are the same
reference. Guards written as `if (existing.Field != input.Field)` are always
false and preserve nothing. Read the previous value from
`db.Entry(existing).OriginalValues` instead. It got past me on
`Item.QuantityOnHand` and again on `ThirdParty.Side`.

---

## An honest note on quality

Where this rebuild is genuinely better than the old app: the approval engine,
the report platform, one database instead of nine, `xmin` concurrency instead of
a hand-rolled token, and tests written alongside the code rather than after.

Where it is worse: **it does not do most of what the business actually does
every day.** Six of seven modules are shallow, and one module is missing
altogether. The old app has years of accumulated decisions encoded in it that
this rebuild has not re-derived — and some of those, particularly in attendance
and payroll edge cases, will only surface when somebody tries to use it.

The strategic option that remains open, and which I recommended before the
rebuild started: **harvest the platform pieces** — approval engine, report
platform, permission model — and port them into the existing app, rather than
finishing a rewrite that is 12–18 sessions from parity. The approval engine in
particular is self-contained and would drop into the old platform without
rewriting a single module.
