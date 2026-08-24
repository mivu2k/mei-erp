# Setting up MEI ERP

Two ways to set this up. Pick the one you actually want:

| You want to… | Read |
|---|---|
| **Run it on a server** so people can use it | [`DEPLOYMENT.md`](DEPLOYMENT.md) — Ubuntu 24.04, systemd, nginx, backups |
| **Work on the code** on your own machine | This document |

Both need the same two things underneath: **.NET 10** and **PostgreSQL 18**.
Ubuntu 24.04 ships neither, which is the single most common reason a first
attempt fails.

---

## 1. Prerequisites

Ubuntu 24.04 (or WSL2 on Windows). Other Linux distributions work; the package
commands differ.

### .NET 10

Ubuntu's own feeds stop at .NET 8:

```bash
curl -fsSL https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb \
  -o /tmp/packages-microsoft-prod.deb
sudo dpkg -i /tmp/packages-microsoft-prod.deb
rm /tmp/packages-microsoft-prod.deb

sudo apt update
sudo apt install -y dotnet-sdk-10.0
dotnet --version          # expect 10.0.3xx
```

`global.json` pins SDK `10.0.302` with `rollForward: latestFeature`, so any
`10.0.3xx` satisfies it. An older SDK is refused rather than silently building
something different.

### ICU

The build sets `InvariantGlobalization=false`, so .NET **hard-fails at startup**
without ICU:

```bash
sudo apt install -y libicu74
```

> `dev.sh` exports an `LD_LIBRARY_PATH` pointing at a locally unpacked ICU. That
> is a workaround for one machine where ICU could not be installed system-wide.
> With `libicu74` installed the line is harmless and does nothing — leave it be.

### PostgreSQL 18

Ubuntu 24.04 ships PostgreSQL 16. Add the PGDG repository:

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

---

## 2. Get the code

```bash
git clone https://github.com/mivu2k/mei-erp.git
cd mei-erp
```

---

## 3. Create the database role

One-time. **Choose a password** and keep it for the next step:

```bash
DB_PASSWORD='pick-something-and-remember-it'

sudo -u postgres psql \
  -c "CREATE ROLE meierp LOGIN PASSWORD '$DB_PASSWORD' CREATEDB;" \
  -c "CREATE DATABASE mei_erp OWNER meierp;"
```

> **`CREATEDB` matters here and only here.** The integration tests each create
> and drop a throwaway database. Without it they **skip** — and a skipping suite
> reports green while asserting nothing, which is exactly the failure this
> project exists to avoid. A production server does not get `CREATEDB`.

Check it:

```bash
PGPASSWORD="$DB_PASSWORD" psql -h 127.0.0.1 -U meierp -d mei_erp -c '\conninfo'
```

---

## 4. Local configuration

`./dev.sh up` copies `appsettings.Development.json.example` to
`appsettings.Development.json` on first run. Do it now so you can fill it in:

```bash
cp host/MeiErp.Host/appsettings.Development.json.example \
   host/MeiErp.Host/appsettings.Development.json
nano host/MeiErp.Host/appsettings.Development.json
```

Set two things:

```jsonc
{
  "ConnectionStrings": {
    "Platform": "Host=127.0.0.1;Database=mei_erp;Username=meierp;Password=YOUR_DB_PASSWORD"
  },
  "Seed": {
    "AdminEmail": "admin@mei.local",
    "AdminPassword": "YOUR_FIRST_PASSWORD"
  }
}
```

- **`appsettings.Development.json` is gitignored.** That line in `.gitignore`
  is load-bearing, not housekeeping — it is the reason no password has ever
  reached the repository. Do not remove it, and do not put secrets in
  `appsettings.json`.
- **`Seed:AdminPassword` is read once**, while creating the first administrator.
  Leave it out and no administrator exists, so you cannot sign in; the log says
  so rather than failing silently.
- Email is off by default. The in-app notification bell works regardless.

---

## 5. Run it

```bash
./dev.sh up
```

First start creates every schema and seeds the first administrator — each module
migrates its own schema as it seeds, so there is no separate migration step.

Open **http://localhost:5090** and sign in with the seed credentials. **You will
be made to change the password immediately.** That applies to the seeded account
too, by design.

Other commands:

```bash
./dev.sh status    # is the database up? is the app up?
./dev.sh down      # stop it
./dev.sh db        # psql shell against mei_erp
./dev.sh test      # the whole test suite
./dev.sh reset     # drop and recreate the database (destructive, asks first)
```

---

## 6. Prove the tests actually run

```bash
./dev.sh test
```

Expect **394 passing, 0 skipped**.

**If you see skipped tests, stop and fix it.** Skips mean the integration suite
could not reach PostgreSQL, so the most consequential code in the system is
asserting nothing while the run reports green. Check the role has `CREATEDB` and
that `MEIERP_TEST_DB` — if you set it — is correct.

Verify the guard works by breaking it deliberately: point `MEIERP_TEST_DB` at a
wrong password and re-run. The suite must report **skipped**, not passed.

---

## 7. First things to configure in the app

1. **Company profile** — `/admin/company`. It prints on every document.
2. **Departments** — `/hr/departments`. Give each a head: a department-head
   approval with nobody set does **not** auto-approve, it escalates, and the
   list flags every department missing one.
3. **Roles and users** — `/admin/roles`, then `/admin/users`.
4. **Approval workflows** — `/admin/workflows`.

---

## Working on the code

Every `dotnet` command needs this environment (`dev.sh` does it for you):

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
```

```bash
dotnet build                                   # whole solution
dotnet test                                    # everything
dotnet test tests/MeiErp.Platform.Workflow.Tests
```

Read [`CLAUDE.md`](CLAUDE.md) before changing anything. It is the design
rationale — the approval engine, the ledger rules, the stock rules, and the
traps that have already cost time. In particular:

- **Some warnings are errors**, including `RZ10012` (a component rendered as an
  unknown HTML element: the page returns 200 with a section silently missing).
- **A module depends on `platform/`, never on another module.**
- **Business failures return `Result`; exceptions are for bugs.**
- **Never read the clock directly** — inject `IClock`.

### One check the compiler will not do for you

Two pages claiming the same `@page` route compiles cleanly, passes every test,
and then throws `AmbiguousMatchException` on **every request** to it. This has
already taken pages down once. Before pushing:

```bash
grep -rhn '@page' --include=*.razor modules/ host/ \
  | sed 's/.*@page *//' | tr -d '"' | sort | uniq -d
```

It must print nothing.

More generally: routing faults and EF query-translation faults both compile and
pass tests while being dead in the browser. A green suite is not evidence a
screen opens — open it.

---

## Troubleshooting

| Symptom | Cause |
|---|---|
| `Couldn't find a valid ICU package` | `libicu74` not installed |
| `dotnet` refuses to build, wrong SDK | `global.json` pins 10.0.3xx; check `dotnet --version` |
| `PostgreSQL is not accepting connections` | `sudo systemctl start postgresql` |
| Tests **skipped** rather than passed | Role lacks `CREATEDB`, or the test connection string is wrong |
| Cannot sign in on a fresh database | `Seed:AdminPassword` was not set, so no administrator was created |
| Page 500s with `AmbiguousMatchException` | Two components claim the same `@page` route — run the check above |
| A screen loads but nothing is clickable | The Blazor circuit did not connect; check the browser console |

---

## Where everything is

```
platform/     the spine — kernel, workflow, persistence, identity,
              notifications, reporting, printing, web (shared UI shells)
modules/      the business modules, one folder each
host/         the one process that composes everything
ops/          deployment, backup, rollback, monitoring
tests/        one project per module and platform piece
```

| Document | What it is |
|---|---|
| [`CLAUDE.md`](CLAUDE.md) | Design rationale and the rules that must not be relaxed |
| [`DEPLOYMENT.md`](DEPLOYMENT.md) | Standing up a server |
| [`ops/RUNBOOK.md`](ops/RUNBOOK.md) | Day-two operations: backup, restore, incidents |
| [`ops/CUTOVER.md`](ops/CUTOVER.md) | Migrating off the old platform |
| [`HANDOVER.md`](HANDOVER.md) | An honest account of how far short of the old app this still is |
