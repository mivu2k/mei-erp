# Deploying MEI ERP on Ubuntu 24.04 LTS

A start-to-finish guide for standing up a **deployment test** server, and the
same steps are what production uses. It assumes a clean Ubuntu 24.04 machine
and a person with `sudo`.

The application already ships its operational tooling in `ops/` — a systemd
unit, an atomic deploy script with automatic rollback, backup and restore
verification, and a health monitor. This guide installs those rather than
inventing a parallel way of doing things. `ops/RUNBOOK.md` is the day-two
reference; read it once you are running.

> **Two things Ubuntu 24.04 does not give you out of the box.** It ships
> .NET 8 and PostgreSQL 16; this application needs **.NET 10** and is developed
> against **PostgreSQL 18**. Both come from vendor repositories below. Skipping
> either is the most common way this install fails.

---

## What you are building

| Piece | Where |
|---|---|
| Application | `/opt/mei-erp/current` → a timestamped release under `releases/` |
| Service | `mei-erp.service`, running as the `meierp` user |
| Listener | `http://127.0.0.1:5090` — **loopback only** |
| TLS / public entry | nginx reverse proxy on 80/443 |
| Secrets | `/etc/mei-erp.env`, mode `640`, `root:meierp` |
| Database | PostgreSQL, one database `mei_erp`, one schema per module |
| Logs | `/opt/mei-erp/current/logs/`, 30-day rolling |
| Backups | `/srv/backup/mei-erp` |

The application never terminates TLS itself and never listens on a public
interface. That is deliberate: it means a misconfigured firewall cannot expose
the app directly.

---

## 1. Base system

```bash
sudo apt update && sudo apt upgrade -y
sudo apt install -y curl ca-certificates gnupg lsb-release ufw nginx git
```

**ICU is required.** The build sets `InvariantGlobalization=false`, so .NET
hard-fails at startup without ICU present:

```bash
sudo apt install -y libicu74
```

Set the machine clock to the business timezone so log timestamps read the way
the office does. The application's own business dates come from
`Platform:TimeZone` (`Asia/Karachi` by default), not from this:

```bash
sudo timedatectl set-timezone Asia/Karachi
```

---

## 2. PostgreSQL 18

Ubuntu 24.04 ships PostgreSQL 16. Add the PGDG repository for 18:

```bash
sudo install -d /usr/share/postgresql-common/pgdg
sudo curl -fsSL https://www.postgresql.org/media/keys/ACCC4CF8.asc \
  -o /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc

echo "deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] \
https://apt.postgresql.org/pub/repos/apt $(lsb_release -cs)-pgdg main" \
  | sudo tee /etc/apt/sources.list.d/pgdg.list

sudo apt update
sudo apt install -y postgresql-18 postgresql-client-18
sudo systemctl enable --now postgresql
```

Create the role and database. **Choose a real password** and keep it — it goes
into `/etc/mei-erp.env` in step 6:

```bash
DB_PASSWORD='replace-with-a-long-random-password'

sudo -u postgres psql \
  -c "CREATE ROLE meierp LOGIN PASSWORD '$DB_PASSWORD';" \
  -c "CREATE DATABASE mei_erp OWNER meierp;"
```

> `CREATEDB` is **not** granted here. Development grants it so the integration
> tests can create throwaway databases; a server that only runs the application
> has no reason to. If you intend to run the test suite on this machine, add
> `CREATEDB` to the role.

Confirm it accepts the credentials:

```bash
PGPASSWORD="$DB_PASSWORD" psql -h 127.0.0.1 -U meierp -d mei_erp -c '\conninfo'
```

---

## 3. .NET 10

Ubuntu 24.04's feeds stop at .NET 8. Use Microsoft's:

```bash
curl -fsSL https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb \
  -o /tmp/packages-microsoft-prod.deb
sudo dpkg -i /tmp/packages-microsoft-prod.deb
rm /tmp/packages-microsoft-prod.deb

sudo apt update
sudo apt install -y dotnet-sdk-10.0
```

`global.json` pins SDK **10.0.302** with `rollForward: latestFeature`, so any
10.0.3xx SDK satisfies it. Verify:

```bash
dotnet --version     # expect 10.0.3xx
```

> **Why the SDK and not just the runtime?** `ops/deploy.sh` builds from source
> on the server. If you would rather keep build tooling off the server, see
> [Publishing elsewhere](#appendix-publishing-from-a-build-machine).

---

## 4. Service account and directories

```bash
sudo useradd --system --create-home --home-dir /var/lib/meierp --shell /usr/sbin/nologin meierp

sudo mkdir -p /opt/mei-erp/releases
sudo chown -R meierp:meierp /opt/mei-erp

sudo mkdir -p /srv/backup/mei-erp
sudo chown meierp:meierp /srv/backup/mei-erp
sudo chmod 750 /srv/backup/mei-erp
```

---

## 5. Get the source

```bash
sudo -u meierp git clone https://github.com/<owner>/<repo>.git /opt/mei-erp/src
cd /opt/mei-erp/src
```

---

## 6. Secrets

Everything sensitive lives in one file outside the repository. **Never** in
`appsettings.json`, never committed.

```bash
sudo cp /opt/mei-erp/src/ops/mei-erp.env.example /etc/mei-erp.env
sudo chown root:meierp /etc/mei-erp.env
sudo chmod 640 /etc/mei-erp.env
sudo nano /etc/mei-erp.env
```

> **Why `root:meierp` and `640` rather than `600`.** systemd reads
> `EnvironmentFile` as root before dropping privileges, so `600` would be enough
> for the service itself. But `ops/backup.sh` and `ops/verify-restore.sh` read
> `MEIERP_DB_PASSWORD` from the environment and run as `meierp` — with a
> root-only file the nightly backup fails on a password it cannot see. Group
> read is the smallest permission that makes both work. It is still unreadable
> to everyone else.

Fill in every `CHANGE_ME`:

```ini
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__Platform=Host=127.0.0.1;Database=mei_erp;Username=meierp;Password=YOUR_DB_PASSWORD
MEIERP_DB_PASSWORD=YOUR_DB_PASSWORD
Platform__TimeZone=Asia/Karachi

Notifications__Email__Enabled=true
Notifications__Email__Host=smtp.example.com
Notifications__Email__Port=587
Notifications__Email__UseStartTls=true
Notifications__Email__Username=YOUR_SMTP_USER
Notifications__Email__Password=YOUR_SMTP_PASSWORD
Notifications__Email__FromAddress=erp@example.com
Notifications__Email__FromName=MEI ERP
Notifications__Email__BaseUrl=https://erp.example.com

# The first administrator, created once on first start.
Seed__AdminEmail=admin@example.com
Seed__AdminPassword=A-strong-first-password
```

Notes that matter:

- `MEIERP_DB_PASSWORD` exists so `ops/backup.sh` and the restore-verification
  script can reach PostgreSQL **without putting a password on a command line**,
  where it would be visible in `ps` to every user on the box.
- `Notifications__Email__BaseUrl` is what links inside emails point at. Get it
  wrong and every password-reset link in every email is wrong.
- **`Seed__AdminPassword` is only read while creating the first administrator.**
  That account is flagged to change its password at first sign-in. If you omit
  it, no administrator is created and you cannot sign in — the log says so
  rather than failing silently.
- For a staging box, `ops/appsettings.Staging.json` redirects **all** outbound
  email to one mailbox. Use it (`ASPNETCORE_ENVIRONMENT=Staging`) so a test
  deployment cannot email real customers.

---

## 7. Install the service and deploy

Install the service and the health monitor:

```bash
sudo cp ops/mei-erp.service /etc/systemd/system/
sudo cp ops/mei-erp-monitor.service ops/mei-erp-monitor.timer /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable mei-erp.service mei-erp-monitor.timer
```

The unit hardens the process: `ProtectSystem=strict` makes the filesystem
read-only apart from `ReadWritePaths=/opt/mei-erp/current/logs`, plus
`NoNewPrivileges` and `PrivateTmp`. Anything else the app needs to write must
be added to `ReadWritePaths` explicitly.

Deploy:

```bash
sudo -u meierp ops/deploy.sh
```

`ops/deploy.sh` publishes a Release build into a new timestamped directory under
`/opt/mei-erp/releases/`, atomically repoints the `current` symlink, restarts the
service, and polls `/health/ready` for 30 seconds. **If readiness does not come
back it rolls itself back automatically** and exits non-zero. A failed deploy
therefore leaves the previous release running rather than a broken one.

The schema is created on first start: each module migrates its own schema as it
seeds. There is no separate migration command to remember.

Check it:

```bash
systemctl status mei-erp --no-pager
curl -fsS http://127.0.0.1:5090/health/live  && echo
curl -fsS http://127.0.0.1:5090/health/ready && echo
```

`/health/live` proves the process answers. `/health/ready` also proves it can
reach PostgreSQL — that is the one to watch.

---

## 8. nginx and TLS

Blazor Server runs over a **WebSocket** for the lifetime of each open page. The
proxy configuration must upgrade the connection and must not cut idle sockets,
or screens will silently stop responding after a minute — the most common
symptom of getting this wrong.

`/etc/nginx/sites-available/mei-erp`:

```nginx
server {
    listen 80;
    server_name erp.example.com;

    location / {
        proxy_pass         http://127.0.0.1:5090;
        proxy_http_version 1.1;

        # Blazor Server's circuit is a WebSocket. Without these the app loads
        # and then goes dead the moment anything interactive is used.
        proxy_set_header   Upgrade    $http_upgrade;
        proxy_set_header   Connection "upgrade";

        proxy_set_header   Host              $host;
        proxy_set_header   X-Real-IP         $remote_addr;
        proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;

        # An idle circuit must outlive nginx's default 60s read timeout.
        proxy_read_timeout  100s;
        proxy_send_timeout  100s;

        # Interactive traffic is small and latency-sensitive.
        proxy_buffering off;
    }

    client_max_body_size 25m;   # document and photo uploads
}
```

```bash
sudo ln -s /etc/nginx/sites-available/mei-erp /etc/nginx/sites-enabled/
sudo rm -f /etc/nginx/sites-enabled/default
sudo nginx -t && sudo systemctl reload nginx
```

TLS via Let's Encrypt (skip if the office terminates TLS elsewhere):

```bash
sudo apt install -y certbot python3-certbot-nginx
sudo certbot --nginx -d erp.example.com
```

Firewall — only nginx is public:

```bash
sudo ufw allow OpenSSH
sudo ufw allow 'Nginx Full'
sudo ufw enable
```

---

## 9. First sign-in

Browse to `https://erp.example.com` and sign in with `Seed__AdminEmail` /
`Seed__AdminPassword`. **You will be required to change the password
immediately** — that is by design, including for the seeded account.

Then, before letting anyone else in:

1. **Company profile** — `/admin/company`. It appears on every printed document.
2. **Departments** — `/hr/departments`. Give each one a head: approvals routed
   to a department head with nobody set do **not** auto-approve, they escalate,
   and the list screen flags every department missing one.
3. **Roles and users** — `/admin/roles`, then `/admin/users`.
4. **Approval workflows** — `/admin/workflows`.

---

## 10. Backups — before you rely on any of this

```bash
sudo -u meierp env $(grep -E '^MEIERP_DB_PASSWORD=' /etc/mei-erp.env | xargs) \
  ops/backup.sh /srv/backup/mei-erp
```

Nightly, via `crontab -e` as `meierp`:

```cron
30 1 * * * set -a; . /etc/mei-erp.env; set +a; /opt/mei-erp/src/ops/backup.sh /srv/backup/mei-erp >> /var/lib/meierp/backup.log 2>&1
```

Cron starts with an almost empty environment, so the file has to be sourced —
without it `backup.sh` has no `MEIERP_DB_PASSWORD` and every nightly run fails
authentication. Run the command by hand once as `meierp` before trusting it.

Copy each `.dump` and its `.sha256` **off the server**. A backup that only
exists on the machine it is protecting is not a backup.

Weekly, prove the newest one actually restores:

```bash
sudo -u meierp env $(grep -E '^MEIERP_DB_PASSWORD=' /etc/mei-erp.env | xargs) \
  ops/verify-restore.sh /srv/backup/mei-erp/<newest>.dump
```

That restores into a disposable `mei_erp_verify_*` database, checks all nine
schemas and the migration history, then drops only that database. Retention:
7 daily, 5 weekly, 12 monthly.

---

## 11. Rehearse the whole thing

Before trusting this server with anything real:

```bash
ops/rehearse.sh
```

It performs a real backup, checksum verification, isolated restore, a Release
publish, a second atomic deployment, a rollback, and a health check. Keep the
output. Per `ops/RUNBOOK.md`, run this before each module cutover.

---

## Routine operations

```bash
# Deploy a new version
cd /opt/mei-erp/src && sudo -u meierp git pull && sudo -u meierp ops/deploy.sh

# Roll back to the previous release
sudo -u meierp ops/rollback.sh

# Logs
sudo journalctl -u mei-erp -f
sudo tail -f /opt/mei-erp/current/logs/mei-erp-*.log
```

**Migrations are forward-only.** Rolling back code is safe only while the schema
is still compatible. After a destructive migration, restore the pre-deploy
backup instead of running older binaries against a newer schema — `ops/deploy.sh`
takes no view on this, so it is on you to know which kind of change you shipped.

---

## Troubleshooting

| Symptom | Cause |
|---|---|
| Service fails instantly, `Couldn't find a valid ICU package` | `libicu74` missing (step 1) |
| `/health/live` passes, `/health/ready` fails | Database unreachable or credentials wrong — check `ConnectionStrings__Platform` |
| Sign-in page loads, then nothing is clickable | nginx not upgrading the WebSocket (step 8) |
| Screens die after ~1 minute idle | `proxy_read_timeout` too low (step 8) |
| Cannot sign in at all on a fresh install | `Seed__AdminPassword` was not set, so no administrator was created — the log says so |
| `deploy.sh` reports rollback | New release failed readiness within 30s; previous release is still serving. Check `journalctl -u mei-erp` |
| Service starts but cannot write logs | `ProtectSystem=strict` — the path must be in `ReadWritePaths` |

---

## Appendix: publishing from a build machine

To keep the .NET SDK off the server, publish elsewhere and ship the output:

```bash
# On the build machine
dotnet publish host/MeiErp.Host/MeiErp.Host.csproj -c Release -o ./publish
rsync -a --delete ./publish/ server:/opt/mei-erp/releases/$(date -u +%Y%m%dT%H%M%S)/
```

Then, on the server, repoint `current` and restart, mirroring what
`ops/deploy.sh` does atomically. The server then needs only the ASP.NET Core
**runtime**, not the SDK:

```bash
sudo apt install -y aspnetcore-runtime-10.0
```

Note that `ops/deploy.sh` assumes it is building from source; if you adopt this
model, adapt the script rather than running both ways at different times.
