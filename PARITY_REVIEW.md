# Legacy parity review ledger

Engineering audit completed 22 August 2026 against `/home/pc/vb/acc`. This ledger separates what automated inspection can prove from the business decisions only an operator can make. Do not mark a module signed off until a real user completes its review box.

| Area | Legacy workflows reviewed | Rebuild evidence | Engineering status | Business review |
|---|---|---|---|---|
| Platform | users, roles, departments, approvals, company settings, notifications, reports, printing | `/admin/*`, `/approvals`, `/reports`; Identity, Workflow, Notifications, Printing and Persistence suites | Complete | [ ] |
| Finance | chart, vouchers/reversal, day book, requests, advances, personal ledger, petty cash, utilities, parties, payroll, reconciliation, close, statements | `/finance/*`; PostgreSQL suite | Complete | [ ] |
| HR | employees, leave, employee documents and expiry, personal attendance/balances, attendance setup/kiosk/punch/correction/register | `/hr/*`; PostgreSQL suite | Complete | [ ] |
| Inventory | hierarchy, items, warehouses, transfers, counts, serial/batch, PO/GRN, SO/delivery, returns, reports | `/inventory/*`; 30 tests | Complete | [ ] |
| Repair | intake, jobs, diagnosis/evidence, workflow, quotation/order/payment, mandatory customer phone, parts, suppliers, purchases, tracking, documents, reports | `/repair/*`; 24 tests | Complete | [ ] |
| Tender | tenders, commercial/detail fields, guarantees, documents, competitor bids, projects, milestones, physical files/movements/scan, reports | `/tender/*`; 20 tests | Engineering parity and guarded importer complete; live source rehearsal remains credential-gated | [ ] |
| Ledger | nested ledgers, balances, movements, paired transfers, statements, reports, business-clock date defaults | `/ledger/*`; 18 tests | Complete | [ ] |
| Gate Pass | inward/outward passes, carrier/company/contact/reference metadata, edit/cancel/detail, clearance segregation, partial returns, demo issuance/partial return/cancel/print | `/gatepass/passes`, `/gatepass/demos`; 19 tests | Complete | [ ] |
| Fleet | vehicle metadata including color/notes, maintenance add/edit/remove, odometer derivation, due reminders, running costs, print | `/auto/vehicles`; 3 tests | Complete | [ ] |

## Guided review procedure

For each row, use the old app and rebuild side by side with the same harmless sample transaction.

1. Confirm every field your team actually uses exists and has the expected meaning.
2. Run the normal start-to-finish workflow, including rejection, correction, cancellation or return where applicable.
3. Compare screen totals, PDF/roll output, Excel export and permission behavior.
4. Record requested changes below. Mark the module business-review box only when the operator accepts it.

## Requested-change ledger

| Status | Module/route | Requested change | Verification |
|---|---|---|---|
| — | — | Add items here during the guided review. Use `TODO`, `DOING`, `DONE`, or `REMOVED`. | — |
| DONE | Tender | Added guarded import mapping for Projects, legacy WorkTasks (split into rebuilt ProjectTasks and tender-scoped TenderTasks), and ProjectMilestones with explicit FK/enum and count reconciliation. | `TenderImporter`; live dry-run still requires read-only MariaDB credentials. |
| DONE | Finance | Restored the legacy Director Fund discriminator on advances, DFR numbering/filtering, director permissions, query-mode UI, capital-account disbursement routing, and Finance migrations. | `Advance.IsDirectorRequest`; `DirectorFundParityTests`; live data reconciliation still requires the guarded importer credential. |
| DONE | Finance | Restored legacy itemized reimbursement requests: multiple categorized lines, per-line reason/details and expense heads, draft editing, validation, persistence, and one ledger debit per item at payment. | `PaymentRequestLine`; `PaymentRequestLineParityTests`; live data reconciliation still requires the guarded importer credential. |
| DONE | Fleet | Restored the legacy vehicle detail route, required model validation, purchase metadata editing, and direct service-history actions; added a safe backfill migration for existing blank models. | `VehicleDetail.razor`; `FleetVehicleModelRequired`; `FleetTests`; business review remains pending. |
| DONE | Fleet | Added the legacy dashboard-equivalent upcoming-maintenance alert and verified date-based due filtering for active/under-repair vehicles. | `UpcomingServicesAsync`; `FleetTests`; business review remains pending. |
| DONE | HR | Restored legacy employee personal, contact, employment, emergency, statutory and bank fields, added sensitive-field permission gating, and applied a persistence migration. | `EmployeeLegacyDetailsParity`; `EmployeeParityTests`; business review remains pending. |
| DONE | Inventory | Restored legacy party payment-term days and notes in the combined customer/supplier master, editor, schema, and persistence coverage. | `PartyTermsParity`; `StockTests`; business review remains pending. |
| DONE | Ledger | Replaced UTC-derived default ledger, entry, and transfer dates with the injected business clock and added a fixed-clock regression test. | `LedgerService`; `PlainLedgerTests`; business review remains pending. |
| DONE | Platform/modules | Migrated business-date defaults, expiry display, approval age, and label-template timestamps from direct machine-clock reads to `IClock`; attendance token generation remains intentionally UTC-based. | `IClock`; full solution build; 373-test suite; business review remains pending. |

Production cutover is deliberately excluded from engineering parity. It requires approved data migration, real SMTP/secrets, TLS/DNS, off-server backups, installed service/timer units, alert routing, operator training and an agreed rollback window.

Legacy-data migration is gated by `ops/CUTOVER.md`. The read-only inventory tools and guarded Identity/company, Fleet, Plain Ledger, Gate Pass, and complete Tender importers are ready. All live rehearsals still require a valid read-only legacy credential.

## Route and behavior audit evidence

The legacy and rebuild `@page` inventories were compared on 22 August 2026. Different page composition is accepted only where the same workflow is present (for example, a rebuild dialog replacing separate legacy new/edit pages).

- Restored omissions found by the audit: `/hr/me` personal attendance and leave balances; `/finance/my-ledger` person-tagged posted entries.
- Preserved legacy bookmarks with authenticated redirects: stock transactions/receipts/parties/reports, Finance reconciliation/admin/approval/payroll/request/voucher bookmarks, HR monthly attendance/code/setup and employee creation, Fleet details/reports, Repair reports/purchase details, Tender project/tender editing, and the merged Inventory detail/report pages.
- Gate Pass accepts the legacy new/detail/outstanding pass URLs as aliases; detail URLs now open the requested record rather than only the list.
- An authenticated live sweep rendered all **129 core declared rebuild routes** with zero HTTP failures. An isolated host HTTP suite now also verifies anonymous health/readiness and a protected legacy alias through the real middleware pipeline. Compatibility aliases cover the additional merged legacy bookmarks; an unauthenticated smoke sweep confirms each alias resolves to the sign-in gate rather than a 404. The sweep also exposed and fixed a deleted/nonexistent Ledger edit bookmark that previously returned HTTP 500.
- Legacy Identity scaffold self-deletion is deliberately replaced by administrator deactivation and last-administrator protection. ERP audit identities are not physically erased. Password, email confirmation, reset, authenticator 2FA, recovery codes and trusted-machine sign-in remain supported.
