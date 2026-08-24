# Unifying buying and selling, and splitting stock in two

Decided 23 August 2026. This supersedes the module boundaries described in
`CLAUDE.md` for Inventory and Repair; update that file as each stage lands.

## The problem

Buying and selling are implemented twice, against two party masters and two
goods masters:

| Concern | Inventory | Repair | Finance |
|---|---|---|---|
| Party master | `Party` (IsCustomer/IsSupplier) | `RepairCustomer` + `RepairSupplier` | `ThirdParty` |
| Buying | `PurchaseOrder` → `GoodsReceipt` | `RepairPurchase` (direct receipt) | — |
| Selling | `SalesOrder` → `Delivery` | `RepairQuotation` → `RepairOrder` → `RepairPayment` | — |
| Goods master | `Item` | `RepairPart` | — |

Two implementations of the same commercial documents means two places to fix a
pricing bug, two definitions of what a customer is, and a supplier who has to be
created twice to be paid once.

## The shape being built

**One `Trade` module at `/trade`** owns every commercial document, for every
part of the business:

- Parties — one customer/supplier master, platform-wide
- Buying — purchase orders → goods receipts → supplier invoices → purchase returns
- Selling — quotations → sales orders → deliveries → invoices → payments → sales returns

**Inventory keeps two stock books, both fully functional.** Not two copies of
the code: one `StockDomain` partition, with `Main Store` and `Spare Parts`
(the workshop's) as rows. Each book has its own items, warehouses, transfers,
counts, serials, valuation and reports. A document, an item and a warehouse each
belong to exactly one book, and stock never moves between books except through a
recorded transfer.

**Repair keeps the workshop only** — intake, jobs, diagnosis, work items,
tracking. A job's quotation and its parts purchasing become Trade documents that
reference the job, drawing parts from the Spare Parts book.

**Plain Ledger is untouched**, by instruction.

## Why a domain partition rather than two modules

The user needs the workshop's spare stock kept apart from main trading stock —
separate valuation, separate reorder levels, separate people. That is a data
boundary, not a code boundary. Two Inventory modules would mean every future fix
applied twice, which is the exact problem this whole exercise exists to remove.
One partition gives the separation without the duplication, and permissions can
be scoped per book so the storekeeper and the workshop never see each other's
stock.

## Stages

Each stage must build clean and keep the suite green before the next starts.

- [x] **0. App-bar quick actions.** `ModuleDescriptor.QuickActions`, rendered by
      `MainLayout`; HR declares the attendance QR code. Any module can now put a
      shortcut on the app bar without the shell referencing it.
- [x] **1. Stock domains.** `StockDomain` seeded with Main Store and Spare
      Parts; `DomainId` on items, warehouses and movements; per-book item codes,
      per-book default warehouse, book-scoped catalogue and stock ledger, a
      cross-book transfer refusal, `StockBookContext` + `StockBookPicker` so the
      chosen book follows the person, and `/inventory/domains` to manage them.
      Migration backfills every existing row into Main Store before the foreign
      keys bite. Five new tests pin the seams.
- [ ] **2. Unified party master.** Trade owns `Party`. Inventory's `Party`,
      Repair's `RepairCustomer`/`RepairSupplier` and Finance's `ThirdParty`
      converge onto it, with a data migration that merges by name and keeps
      Finance's receivable/payable ledger account hanging off the party.
- [x] **3. Trade module — buying.** `modules/Trade` at `/trade`, schema `trade`,
      in the nav as **Sales & Purchase**. Purchase orders and goods receipts
      moved out of Inventory, each order carrying the stock book it buys into.
- [x] **4. Trade module — selling.** Sales orders and deliveries moved out of
      Inventory, with the cost snapshot, the no-reservation rule and the
      serial/batch rules intact.
- [x] **2a. Party master.** Trade owns the single `Party`; Inventory's list is
      copied across by the `TradeInitial` migration and its pages are gone.
      Absorbing Finance's `ThirdParty` is still outstanding — see stage 2 below.

  **Still to finish this stage:** Inventory's own `PurchaseOrder`, `SalesOrder`,
  `GoodsReceipt`, `Delivery`, `Party` and `InventoryReturn` entities, services
  and tables are still present as dead code — nothing reaches them, but they are
  duplication until removed. That means moving ~10 tests and 3 report
  definitions to Trade first, then a migration to drop the old tables. Repair's
  `RepairPurchase` and quotation → order → payment have not folded in yet.
- [ ] **5. Goods master.** `RepairPart` folds into `Item` in the Spare Parts
      book, so workshop parts carry real stock rather than cost alone.
- [x] **6. Routes, reports, printing, tests.** Legacy `/inventory/*` and
      `/repair/*` commercial bookmarks redirect to their new homes, report
      catalogs and print endpoints follow, and the suite covers the moved
      behaviour in its new location.
- [x] **7. The document chain.** `Quotation` and `Invoice` in both directions,
      sharing one implementation with a `TradeDirection` discriminator: draft →
      submit → approval → approved/posted, with the value driving who signs.
      `DocumentShell` renders one editor as either a dialog or a page, with
      **Save draft** and **Submit/Post** as separate actions.

- [x] **8. Repair's commercial side retired.** Its customers, quotations, orders
      and payments are gone; the workshop reads the one party master through
      `IRepairCustomerDirectory` and its jobs carry a party id plus a name
      snapshot rather than a foreign key. Repair quotations became `Quotation`s
      with a job reference, repair orders became posted `Invoice`s carrying what
      was collected as `AmountSettled`, and the six money reports moved to Sales
      and Purchase. **Quote this job** on a job, and the quote icon on an intake,
      raise a Sales quotation straight from the recorded billable work.

## Not done yet

Named honestly so nobody assumes otherwise:

- **Quotation → order conversion** — `Quotation.ConvertedToOrderId` exists but
  nothing fills it yet.
- **Invoice settlement** — `AmountSettled` is modelled and the balance reads off
  it, but there is no receipt/payment screen feeding it.
- **Finance's `ThirdParty`** is still separate from `Party`.
- **HR centralisation and the wider UI rework** have not been started.
- **Reports**: the parts-buying reports moved, but quotations and invoices have
  no report definitions registered yet.

## Rules carried forward

These held in the old code and must survive the move.

- An order is a commitment; only the second document moves stock. Confirming a
  sales order reserves nothing.
- A delivery snapshots unit cost at posting time from the weighted average.
- A serialised line names exactly the serials it ships, and must not also be
  quantity-adjusted.
- Posting a delivery opens no transaction of its own; the pre-flight loop checks
  every line before moving any of it.
- A quotation carries two independent approvals, customer and manager; a sales
  order copies amounts off it rather than referencing it.
- Nothing in the stock books posts to Finance's ledger.
