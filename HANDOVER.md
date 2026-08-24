# MEI ERP rebuild — handover

Written 22 August 2026, at the end of the first build session. Read this before
`CLAUDE.md`; that file explains *how* the code works, this one explains **where
the project actually stands and what to do next**.

## Resume checklist

This is the authoritative development queue. Start every future session at the
first unchecked item, mark an item complete only after its tests and build pass,
and keep the old app available as the behaviour reference until parity is signed
off module by module.

- [x] Company profile administration
- [x] Durable in-app and email notifications
- [x] Approval SLA reminders and role escalation
- [x] Notification preferences, dead-letter review, and branded email templates
- [x] HR attendance: stations, kiosk, NFC, rotating QR, punches, corrections,
      leave precedence, monthly register, and recomputation
- [x] Plain Ledger module
- [x] Outbox dispatcher and cross-module Finance posting
- [x] Per-record printing and labels across every module
- [x] Repair commercial workflow, purchasing, tracking, printing, and reports
- [x] Inventory warehouses, transfers, counts, serials, returns, and reports
- [x] Tender physical file registry and milestones
- [x] Global search, audit viewer, live dashboard, and report-platform depth
- [x] Account security: reset email, confirmation, and 2FA
- [x] Backups, staging, monitoring, deployment rehearsal, and rollback plan
- [ ] Old/new page and workflow parity audit completed with business sign-off
      (engineering audit is complete in `PARITY_REVIEW.md`; user sign-off is pending)
- [ ] Capture the nine legacy MariaDB schemas/counts with a read-only account,
      build and rehearse the mapped importer, and sign off reconciliation totals
      (`ops/CUTOVER.md`; guarded Identity/company, Fleet, Plain Ledger, Gate
      Pass, and Tender importers are ready, but the configured development
      source credential is currently rejected)
- [ ] Production cutover completed module by module

Last verification: 23 August 2026 — all 373 tests passed; full solution build
succeeded with 0 warnings and 0 errors; development app healthy on port 5090;
backup/restore, release, rollback, staging-start and health rehearsal passed.
Legacy account and merged module bookmarks now resolve through compatibility
redirects, with protected routes preserving the sign-in gate.

---

## The honest summary

**This rebuild contains all eight module boundaries and the major legacy-depth
work for all modules. The engineering legacy-parity audit is complete; remaining
work is guided business sign-off and production cutover.**

| | Old app (`/home/pc/vb/acc`) | This rebuild | |
|---|---|---|---|
| Code (excl. migrations) | 57,065 | 31,778 | **56%** |
| Pages | 175 | 121 | 69% |
| Tests | 226 | 373 passing cases | **165%** |
| Modules | 8 | 8 | All module boundaries present |

**The old app at `/home/pc/vb/acc` must stay available for comparison.** Nothing
here is authorized to replace it until business sign-off; deployment, restore,
staging and rollback have been rehearsed.

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
  from it so they cannot disagree. Catalogs are registered across all eight
  modules, including the audited legacy Tender, Ledger, Fleet, and Gate Pass
  subjects. Users can save named/default filter views and schedule real report
  runs daily, weekly, or monthly in the configured business timezone. Every
  scheduled run rechecks permission, records its result/error and queues an
  in-app/email delivery through the durable notification pipeline.
- **Printing** — QuestPDF documents in A4 / 80mm / 62mm, Code 128 and QR that
  round-trip through a real decoder, real Excel workbooks with numbers as
  numbers.
- **Persistence** — PostgreSQL, one database, schema per module, audit stamping,
  soft delete, `xmin` concurrency, outbox table (unused so far).
- **Host HTTP smoke gate** — an isolated PostgreSQL `WebApplicationFactory`
  verifies anonymous liveness/readiness and that protected legacy aliases retain
  the sign-in redirect (`MeiErp.Host.Tests`, 2 tests).

### Finance — complete against the old app

Chart of accounts · vouchers with reversal · day book · payment requests ·
including legacy-style itemized reimbursement lines with per-line expense heads
and voucher posting ·
advances with the full disburse → justify → settle flow · petty cash · utilities ·
third parties with statements · payroll with pro-rating and advance recovery ·
bank reconciliation · year close · 6 reports · PDF and Excel export. Director
Funds are a distinct advance mode with DFR numbering, director permissions, and
disbursement against the dedicated Director capital head.

**90 tests.** This is the one module I would defend as production-shaped.

### Module-depth status

| Module | What exists | What is missing |
|---|---|---|
| HR | Employees with legacy personal/contact/employment/bank details, leave, employee documents/files and expiry, stations, kiosk/NFC/rotating QR attendance, corrections, monthly register, payroll attendance integration | Business sign-off and cutover rehearsal |
| Inventory | Product family→model→accessory catalog, items, warehouse-aware ledger and balances, two-stage transfers with shortage visibility, posted counts, serialized units, batch/expiry tracking, PO→receipt, SO→delivery, purchase/sales returns, parties with payment terms/notes, and all 8 legacy report subjects | No known functional gap against the audited legacy Inventory scope; business sign-off remains required |
| Repair | Jobs/state machine/history, structured diagnoses and private photo evidence, work items, richer customer/delivery capture, mandatory customer phone parity, multi-device intakes with catalog selections and payment basis, individual and collective quotations, customer orders/payments, parts/suppliers/purchases, tracking, scan, all 15 legacy report subjects plus 2 operational registers, the legacy document set, and configurable device-label templates | No known functional gap against the audited legacy Repair scope; business sign-off remains required |
| Fleet | Vehicles including legacy color/notes, required make/model, purchase metadata, detail workflow, service history add/edit/remove, upcoming-maintenance alerts, running costs, odometer derivation, expiry alerts | No known functional gap; business sign-off remains |
| Gate Pass | Passes with legacy carrier/company/contact/reference metadata, edit/cancel/detail, partial returns, segregation of duties, demo issuance/partial returns/cancellation/printing | No known functional gap; business sign-off remains |
| Tender | Tenders, commercial/detail fields, item and guarantee detail, document metadata, competitor bids, projects, task board, milestones, all 6 legacy report subjects, and one automatically created physical file per tender/project with guarded movements, overdue visibility, scan lookup, stickers, and movement registers | Engineering parity and guarded importer are complete; live source rehearsal remains credential-gated |

---

## What is missing — ordered by how much it matters

### 1. Notifications — complete

`Platform.Notifications` exists and the approval engine is wired to it, so
raising a request now tells the people who can approve it, and settling one
tells the raiser. In-app and email channels, a durable queue with exponential
backoff, per-user per-category preferences, a user preferences screen, an
authorized dead-letter review/retry screen, and company-branded HTML email with
a plain-text alternative. Approval SLA reminders and role escalation run from a
five-minute background sweep and are recorded in the approval history.

### 2. Attendance — complete

The HR module now has shift calendars and weekly offs, holiday administration,
stations, employee NFC/QR enrollment, immutable punches, derived attendance
days, and the PostgreSQL migration. It includes pure attendance calculation,
rotating HMAC credentials, keyboard/NFC and webcam QR kiosk capture with
debounce, deterministic rebuilding, leave precedence, protected manual
corrections that survive rebuilding, employee-scoped privacy, daily and monthly
registers, PDF/Excel exports, and the personal rotating QR page. Approved leave
refreshes affected attendance and payroll consumes payable attendance days for
pro-rating. The HR suite has 41 green tests, including PostgreSQL integration
and QR-frame decoding coverage.

### 3. Plain Ledger module — complete

The old module has been ported into the shared rebuild shell and PostgreSQL
architecture: main and sub-ledgers with unlimited nesting, payable/receivable
nature, opening and running balances, own versus descendant roll-up totals,
external movements, transactional linked-pair transfers, protected amendment
and deletion of both transfer halves, nested module-specific heads, filters,
statements, dashboards, and reports. Six permissions and three role templates
are registered. All seven workflow pages are live and 18 PostgreSQL parity tests
cover the old module's essential invariants.

### 4. The outbox / cross-module posting

**Complete:** `Platform.Messaging` dispatches durable per-module outbox
rows, retries five times, dead-letters failures, and supports authorized manual
retry. Finance has configurable debit/credit posting rules and explicitly keyed
idempotent system vouchers. Inventory goods receipts stage stock, receipt,
order, and outbox changes in one atomic save, then post the payable asynchronously.
Posting-rule and failure-review screens are live. Repair approved quotations
become immutable customer orders and emit receivable events through the same
path. Both Finance consumers use explicit idempotency keys, so dispatcher replay
cannot double-post. Three dispatcher tests, three Finance integration tests, and
Inventory/Repair PostgreSQL coverage are green. Additional producers can use
the same bus without changing the dispatcher.

### 5. Module depth

Per the table above. Repair has quotations with dual approval, customer orders, payments, receivable
posting, a parts catalog, supplier registry, purchase receiving, and
last/weighted-average part-cost tracking, atomic multi-device intake grouping,
a stage-aging tracking board, barcode/serial scan navigation, editable intake
catalogs wired into intake, structured diagnosis history, append-only state
history, private photo/PDF evidence, and all 15 legacy report subjects through
the shared screen/PDF/Excel pipeline (plus job and diagnosis registers). The
legacy intake/job/delivery/quotation/invoice/purchase document set is live.
Collective intake quotations now combine every device's billable work into one
customer quotation and order. Intake payment basis, organization/contact notes,
communication preference, and collector phone/CNIC/release notes are persisted.
Administrators can define device-label stock dimensions, field order, font scale,
barcode/QR visibility and a default template; batch intake printing emits one
physical label page per device. Repair has no known gap against the audited
legacy scope, but still requires business-user sign-off.

Inventory now has named/default warehouses, per-location balances, safe deletion
rules, warehouse-attributed receipts/deliveries/adjustments, draft → in-transit →
received transfers, recorded short receipts, and auditable stock-take documents
whose variances post once through the stock ledger. The warehouse migration
backfills existing single-location quantities into `MAIN`. Inventory now also
organizes stock as product family → model → accessory without duplicating the
SKU ledger. Receipts enforce exact unique serials or required batches according
to item configuration; deliveries reject unavailable serials, and batch issues
consume earliest expiry first. `/inventory/products` and
`/inventory/tracking` provide management and audit views. Purchase and sales
returns now require reasons, preserve serial/batch invariants, post atomically
through the stock ledger, and produce printable return notes. All 8 legacy
Inventory report subjects are registered in the shared screen/PDF/Excel
pipeline and execute against PostgreSQL. Inventory has no known gap against the
audited legacy scope, but still requires business-user sign-off.

Tender now creates and backfills one numbered physical folder for every tender
and project. Its append-only register covers issue, return, direct transfer,
archive/reopen, lost/found, holder, purpose, due-back, shelf/volume, and actor
snapshots. `/tender/files` provides filtering and overdue visibility;
`/tender/files/scan` resolves printed numbers. Authenticated sticker and movement
register PDFs are linked from each file. Projects now carry ordered milestones
with pending/achieved/missed/cancelled state, achieved-date reconciliation,
payment stages, and overdue visibility.

### 6. Per-record printed documents

**Complete for the currently audited record set:** authenticated, branded PDF endpoints cover Gate Pass
(A4/roll), Repair job card/device label/delivery note/quotation/invoice/receipt,
Inventory purchase order/goods receipt/sales order/delivery/return, Finance vouchers
and payslips, Tender summaries/file stickers/file movement registers, Auto vehicle histories, HR employee profiles,
and Ledger statements. Each is linked from its record screen. The shared renderer
is covered for A4, roll and label output. Repair intake A4/roll receipts,
device labels, job cards/labels, delivery notes, quotations,
invoices/receipts, and purchase notes are now live.

### 7. Platform gaps

- **Global search** — live in the app bar across all eight modules, permission-filtered.
- **Audit trail** — every module writes created/modified/soft-deleted evidence
  with actor, timestamp, record id, and redacted before/after JSON into one
  append-only platform table in the same transaction as the business change.
  `/admin/audit` provides permission-gated module/entity/user/date filtering.
- **Account security** — enumeration-safe self-service password reset with
  two-hour single-use links, required confirmation for new/changed addresses,
  confirmation resend, authenticator-app 2FA, trusted devices, and one-time
  recovery codes. Administrator-created users receive confirmation invitations;
  SMTP must be configured outside source control for real delivery.
- **Self-service parity** — `/hr/me` shows the signed-in employee's monthly
  attendance and leave balances with server-side privacy filtering;
  `/finance/my-ledger` shows only posted voucher lines tagged to that login.
  Legacy bookmarks redirect to their rebuild equivalents, and all 129 declared
  UI routes pass an authenticated live HTTP sweep.
- **Dashboard** now reads live approval, payment, repair, reorder, gate-return,
  physical-file, and tender metrics; richer trend charts remain future depth.
- **Operations** — `ops/backup.sh` produces checksummed custom-format dumps;
  `verify-restore.sh` restores only to a guarded throwaway database and verifies
  all nine schemas plus migration history. Immutable timestamped Release
  publishing, atomic symlink deployment, automatic/manual rollback, systemd
  service/timer templates, live/readiness monitoring, staging email redirection,
  secret templates, and an incident runbook are present. `ops/rehearse.sh` has
  been executed successfully against a real backup/restored database, including
  a published Staging process on port 5190. Installing the supplied units,
  off-server retention, TLS/DNS, and alert routing belong to production cutover.
  Read-only legacy/rebuild inventory tools and the cross-engine reconciliation
  gate are documented in `ops/CUTOVER.md`; guarded Identity/company, Fleet, Plain
  Ledger, Gate Pass, and Tender importers are ready. Live MariaDB
  capture/rehearsal and reconciliation require a valid read-only source
  credential.

---

## Known bugs and rough edges

- Global search intentionally caps results per record type and 30 overall; a
  dedicated full-results page and ranking telemetry are not yet implemented.
- Report row drill-through is live wherever a source-record target exists;
  aggregate rows intentionally remain non-clickable when no single source record exists.
- `PayrollEmployee` duplicates HR's `Employee`. Deliberate for now (payroll must
  work without HR installed) but they will need reconciling.
- Payroll attendance pro-rating is supplied from HR when that module is
  installed; Finance still remains independently usable without HR.

---

## Where to pick up

In order. Each is a session's work or less unless noted.

1. ~~**Build `/admin/company`**~~ — **done.** The page exists at
   `/admin/company`; two bugs under it were fixed on the way (`SaveAsync`
   clobbered the primary key, and `GetAsync` handed the process-wide cache to
   the caller). Six tests.
2. ~~**Notifications and email**~~ — **done.** `Platform.Notifications`
   exists: channel abstraction (in-app + email over MailKit), a durable queue
   with backoff and a dead-letter state, per-user per-category preferences, the
   bell in the app bar, and the approval engine wired to raise on assignment and
   on settlement. Preferences and dead-letter review screens, approval SLA
   handling, and branded HTML/plain-text templates are included. 27 tests.
3. ~~**Scheduler + SLA reminders and escalation**~~ — **done.** A five-minute
   background sweep sends one reminder per overdue step, escalates to the
   configured role, makes that role eligible to act, and records escalation in
   the append-only approval history.
4. ~~**Attendance**~~ — **done.** Full kiosk-to-payroll workflow, administration,
   corrections, employee documents/expiry, exports, and automated rebuilding are implemented. 41 HR tests.
5. ~~**Plain Ledger module**~~ — **done.** The complete old design is ported into
   the shared host, with its PostgreSQL migration and 17 parity tests.
6. ~~**Outbox and cross-module posting**~~ — **done.** Durable dispatch, retries,
   dead-letter administration, configurable rules, and idempotent Inventory
   payable/Repair receivable postings are live.
7. ~~**Per-record printing**~~ — **done for the audited record set.** Tender file
   stickers and movement registers complete the previously absent record type.
8. ~~**Repair depth**~~ — **done against the audited legacy scope.** Workshop,
   commercial, procurement, evidence, collective intake quotation, payment basis,
   richer customer/delivery capture, configurable labels, and report parity are live.
9. ~~**Inventory depth**~~ — **done against the audited legacy scope.** Warehouses,
   transfers, posted counts, product hierarchy, serialized units, batch/expiry
   tracking, purchase/sales returns, printable return notes, and all 8 legacy
   report subjects are live with PostgreSQL coverage.
10. ~~**Tender file registry and milestones**~~ — **done.** Automatic/backfilled
    files, guarded append-only movements, overdue filters, scan lookup, sticker
    and register printing, and project milestone management are live.

The remaining work is tracked by the unchecked checklist above; do not estimate
cutover readiness until those verification and operational items are proven.

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

PostgreSQL 18, database `mei_erp`, role `meierp`. Nine schemas: `platform`,
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

Where it is still weaker is operational proof: the engineering gates now cover
host HTTP routing, business-clock boundaries, security headers and deployment/
backup rehearsal, but no module has received formal business sign-off or
production cutover.
