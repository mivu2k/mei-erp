# Module cutover and legacy-data gate

The legacy application stores Identity and eight modules in separate MariaDB databases. The rebuild stores them in one PostgreSQL database with nine schemas. A backup/restore rehearsal of the rebuild does **not** prove that legacy records can be migrated.

## Required evidence before writing any legacy data

1. Create or nominate a MariaDB account with `SELECT` and no write privileges across the nine legacy databases.
2. Run `LEGACY_MYSQL_PASSWORD=... ops/legacy-inventory.sh /secure/path/legacy-inventory`.
3. Run `MEIERP_DB_PASSWORD=... ops/rebuild-inventory.sh /secure/path/rebuild-inventory`.
4. Retain both `SHA256SUMS` files with the cutover record. Do not email raw exports containing staff, CNIC, payroll or customer data.
5. Build the importer from the captured column inventory. Every table mapping must specify source key, target key, enum conversion, nullable/default behavior, actor/timestamp preservation and a rejection rule.

## Implemented importers

Guarded importers are implemented for:

- Fleet: `erp_auto.Vehicles` and `erp_auto.MaintenanceRecords`, including
  explicit enum conversions and odometer range checks.
- Plain Ledger: `erp_ledger.Heads`, `Ledgers`, and `Entries`, including hierarchy,
  foreign-key, enum, positive-amount, and reciprocal transfer-pair validation.
- Gate Pass: `erp_gatepass.GatePasses`, `GatePassItems`, `DemoIssuances`, and
  `DemoIssuanceItems`, including explicit direction/status conversions,
  return-state derivation, foreign-key and quantity validation.
- Identity/company: ASP.NET users, roles, claims, logins, tokens, module
  overrides, and company profiles, preserving password hashes and source IDs.
- Tender: `Tenders`, `Guarantees`, `Documents`, `Competitors`, and
  `TenderItems`, including explicit status/type conversions, foreign-key,
  amount, enum, and count validation.

The Tender importer is implemented but remains gated like the others: its
default mode is read-only, and apply requires an empty target plus explicit
confirmation and PostgreSQL credentials. It maps Projects, legacy WorkTasks
(split into rebuilt ProjectTasks and tender-scoped TenderTasks), and
ProjectMilestones as well as the tender, guarantee, document, competitor and
item tables; unknown or invalid rows are rejected rather than discarded.

Their default mode is read-only validation and a JSON count/rejection report:

```bash
LEGACY_MYSQL_PASSWORD='...' dotnet run --project tools/MeiErp.LegacyImport -- --module auto
LEGACY_MYSQL_PASSWORD='...' dotnet run --project tools/MeiErp.LegacyImport -- --module ledger
LEGACY_MYSQL_PASSWORD='...' dotnet run --project tools/MeiErp.LegacyImport -- --module gatepass
LEGACY_MYSQL_PASSWORD='...' dotnet run --project tools/MeiErp.LegacyImport -- --module identity
LEGACY_MYSQL_PASSWORD='...' dotnet run --project tools/MeiErp.LegacyImport -- --module tender
```

An apply run additionally requires the PostgreSQL password and both explicit
write flags. Each module importer refuses a non-empty target, writes in one
transaction, preserves source IDs and audit timestamps, maps every legacy enum
explicitly, and reconciles target counts before commit. Numeric sequences are
reset where the target schema uses them.

```bash
LEGACY_MYSQL_PASSWORD='...' MEIERP_DB_PASSWORD='...' \
  dotnet run --project tools/MeiErp.LegacyImport -- \
  --module ledger --apply --confirm-empty-target
```

Run every dry-run and apply command against isolated staging first. Live source
inventories, field mappings, and reconciliation totals still require operator
approval before production cutover.

## Migration order

Identity/company/departments/users → Finance chart/opening books → HR employees/setup → Inventory catalog/parties/warehouses/opening stock → Repair masters/open jobs → Tender/files → Gate Pass outstanding returns → Fleet/maintenance → Plain Ledger.

Each module runs first into an isolated restored PostgreSQL staging database. Reconcile source/target counts, financial control totals, outstanding quantities and sampled documents before the business owner checks that module in `PARITY_REVIEW.md`.

## Go/no-go controls

- Freeze legacy writes and record the exact freeze timestamp.
- Take checksummed MariaDB dumps for all nine legacy databases and a verified PostgreSQL pre-cutover backup.
- Run the importer from immutable release artifacts; never edit production rows manually.
- Require zero rejected rows unless every rejection is documented and signed off.
- Reconcile Finance debits=credits and closing balances; Inventory on-hand by SKU/warehouse; employee/leave balances; open Repair jobs and receivables; outstanding Gate Pass/demo returns; active guarantees/files; Fleet expiry/odometer; Ledger own and roll-up balances.
- Run the 129-route authenticated smoke sweep and representative PDF/Excel output after migration.
- Keep the legacy system read-only through the rollback window. Roll back by switching users to legacy and restoring the pre-cutover PostgreSQL backup; never attempt reverse synchronization during the window.

## Current blocker

On 22 August 2026 the configured development MariaDB login was rejected. No privileged fallback was attempted. Guarded Identity/company, Fleet, Plain Ledger, Gate Pass, and complete Tender importers are implemented. All live dry-runs/reconciliation require a user-provided read-only legacy credential.
