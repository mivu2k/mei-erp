# MEI ERP

One integrated operations suite, rather than eight applications behind a shared
login. HR, Finance, Inventory, Sales, Purchase, Fleet, Gate Pass, Repair, and
Tender & Projects, on one database with one approval engine and one report
platform.

Built on .NET 10, Blazor Server and PostgreSQL 18.

---

## Start here

| You want to… | Go to |
|---|---|
| **Set it up on your own machine** | [`SETUP.md`](SETUP.md) |
| **Deploy it to a server** | [`DEPLOYMENT.md`](DEPLOYMENT.md) |
| **Change the code** | [`CLAUDE.md`](CLAUDE.md) first — it is the design rationale |
| **Run it day to day** | [`ops/RUNBOOK.md`](ops/RUNBOOK.md) |

Quick version, once the prerequisites in [`SETUP.md`](SETUP.md) are in place:

```bash
git clone https://github.com/mivu2k/mei-erp.git
cd mei-erp
./dev.sh up          # http://localhost:5090
```

---

## Status

All seven business modules are in and the app runs, with **394 tests green** and
no build warnings.

**Nothing is in the office yet.** The old platform stays live until this reaches
parity feature by feature, module by module, with no big-bang switch.
[`HANDOVER.md`](HANDOVER.md) is the honest account of how far short it still is.

Not built yet: the outbox event bus that would let Inventory and Repair post to
Finance's ledger automatically, and the scheduler behind the approval engine's
SLA reminders.

---

## What is deliberately different from the old platform

Each of these is a correction to something that cost real time. The reasoning is
in [`CLAUDE.md`](CLAUDE.md); the short version:

- **One PostgreSQL database, one schema per module** — not nine MariaDB
  databases. Reports can join across modules, one backup covers everything, and
  a cross-module write fits in one transaction.
- **One approval engine**, not nine hand-rolled status enums. Versioned
  definitions, amount-based routing, delegation, return-for-correction, and a
  document matching no step is **refused, never auto-approved**.
- **Everything posts to the ledger.** No module writes financial state directly.
  A posted voucher is immutable; the correction path is a reversal.
- **`StockService` is the only thing that moves stock**, and the movement
  history is the truth.
- **`Result<T>` for business failures**, exceptions only for bugs.
- **Tests from the first commit.** The old Finance module — the most
  consequential code in it — had twelve.

---

## Layout

```
platform/     the spine — kernel, workflow, persistence, identity,
              notifications, reporting, printing, web (shared UI shells)
modules/      the business modules, one folder each
host/         the one process that composes everything
ops/          deployment, backup, rollback, monitoring
tests/        one project per module and platform piece
```

A module depends on `platform/`, never on another module. That rule is what
keeps this comprehensible.
