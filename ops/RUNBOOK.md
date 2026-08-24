# MEI ERP operations runbook

Keep production secrets in `/etc/mei-erp.env` with mode `600`, never in source control. `MEIERP_DB_PASSWORD` supplies PostgreSQL client tools without putting a password on their command line. Install the supplied service and monitoring timer under `/etc/systemd/system`. Terminate TLS at the office reverse proxy; the application listens on loopback only.

## Backup and restore

Run `ops/backup.sh /srv/backup/mei-erp` nightly and copy each `.dump` plus `.sha256` off-server. Retain 7 daily, 5 weekly and 12 monthly copies. Test the newest backup weekly with `ops/verify-restore.sh BACKUP`; it restores to a disposable `mei_erp_verify_*` database, validates all nine schemas and migration history, then removes only that database.

## Deploy and rollback

Run tests/build and take a verified backup. `ops/deploy.sh` publishes an immutable timestamped release, atomically switches `current`, restarts the service and automatically rolls back if readiness does not recover in 30 seconds. Manual rollback is `ops/rollback.sh`. Migrations are forward-only: after a destructive migration restore the pre-deploy backup rather than running older binaries against a newer schema.

Legacy MariaDB migration and module reconciliation are separate from deploy/rollback. Follow `ops/CUTOVER.md`; a rebuild backup rehearsal is not evidence that legacy records were transferred correctly.

## Monitoring and incidents

`/health/live` proves the process responds; `/health/ready` also proves PostgreSQL access. The timer checks both every minute; connect failed-unit events to the office alerting system. Structured logs have 30-day retention. During an incident preserve logs, stop writes, record the release, roll back code only when schema-compatible, otherwise restore the pre-deploy backup into a new database and switch only after verification.

## Rehearsal

With development PostgreSQL and the app running, `ops/rehearse.sh` performs a real custom-format backup, checksum verification, isolated restore, Release publish, second atomic deployment, rollback, and health check. Retain its output before each module cutover.
