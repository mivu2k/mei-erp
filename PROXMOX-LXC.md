# Deploy MEI ERP in a Proxmox LXC

This guide covers the Proxmox-specific preparation. Inside the container, use
the standard [`DEPLOYMENT.md`](DEPLOYMENT.md) procedure; the supplied systemd,
deployment, backup, monitoring, and rollback tools work normally in LXC.

## 1. Create the container

Download an **Ubuntu 24.04** LXC template on the Proxmox host, then create an
unprivileged container. A practical starting size for a small office is:

- 4 CPU cores
- 8 GB RAM and 2 GB swap
- 64 GB root disk on SSD-backed storage
- A static LAN address or DHCP reservation
- Start at boot enabled
- Unprivileged container enabled; nesting is not required

Example on the Proxmox host (replace storage, bridge, password, template, and
network values):

```bash
pct create 120 local:vztmpl/ubuntu-24.04-standard_24.04-2_amd64.tar.zst \
  --hostname mei-erp --unprivileged 1 --cores 4 --memory 8192 --swap 2048 \
  --rootfs local-lvm:64 --net0 name=eth0,bridge=vmbr0,ip=192.168.1.50/24,gw=192.168.1.1 \
  --nameserver 192.168.1.1 --onboot 1
pct start 120
pct enter 120
```

Use a fixed DHCP reservation instead of the static `ip`/`gw` values if that is
how your network is managed. Do not expose PostgreSQL port 5432 to the LAN.

## 2. Prepare Ubuntu inside the LXC

```bash
apt update && apt full-upgrade -y
apt install -y sudo openssh-server
adduser erpadmin
usermod -aG sudo erpadmin
timedatectl set-timezone Asia/Karachi
hostnamectl set-hostname mei-erp
reboot
```

Add your SSH public key to `/home/erpadmin/.ssh/authorized_keys` (preferred), or
use the password created by `adduser`. After the container returns, SSH as
`erpadmin` to its LAN address and complete every section
of [`DEPLOYMENT.md`](DEPLOYMENT.md), starting at **Base system**. The short
sequence is:

1. Install nginx, PostgreSQL 18, .NET 10, Git, ICU, and firewall tools.
2. Create the `meierp` database role and `mei_erp` database.
3. Create the `meierp` service account and `/opt/mei-erp` directories.
4. Clone `https://github.com/mivu2k/mei-erp.git` into `/opt/mei-erp/src`.
5. Create `/etc/mei-erp.env`, including database, SMTP, and first-admin values.
6. Install the supplied systemd service and monitoring timer.
7. Run `sudo -u meierp -H dotnet restore`, then `sudo -u meierp ops/deploy.sh`.
8. Configure nginx and TLS, then verify `/health/live` and `/health/ready`.

If nginx/TLS is handled by a separate reverse-proxy LXC, permit port 80 only
from that proxy and proxy to `http://MEI_ERP_LXC_IP`. In that arrangement the
application service must listen on the LXC address instead of loopback; change
the systemd `ExecStart` URL to `http://0.0.0.0:5090`, run `systemctl
daemon-reload`, and restrict port 5090 at both the Proxmox and container
firewalls to the proxy's IP. Keeping nginx inside this LXC is simpler and uses
the supplied secure loopback-only default.

## 3. Proxmox firewall

Allow only:

- TCP 22 from the administration network
- TCP 80 and 443 from users, or only from the reverse-proxy LXC
- Established/related traffic and required outbound DNS, HTTP, HTTPS, NTP, and
  SMTP traffic

Do not allow inbound 5090 or 5432 when nginx and PostgreSQL are in this same
container.

## 4. Persistent storage and backups

The database is the primary business data store. Repair photos and restore
safety archives live below `/opt/mei-erp/shared`; do not exclude that directory
from backups.

Use both backup layers:

1. Run `ops/backup.sh` nightly and copy its `.dump` and `.sha256` files to
   storage outside this LXC. Verify the newest dump weekly with
   `ops/verify-restore.sh`.
2. Configure a scheduled Proxmox Backup Server job (preferred), or vzdump, for
   the complete container. Snapshot mode is useful for fast disaster recovery,
   but the PostgreSQL dump remains the portable, application-level backup.

Never keep the only backup on the same Proxmox node. Before a major update,
take an application backup and a Proxmox snapshot. Remove old snapshots after
the update is proven; snapshots are not long-term backups.

## 5. Deploy updates from GitHub

Inside the LXC:

```bash
cd /opt/mei-erp/src
sudo -u meierp git pull --ff-only origin main
sudo -u meierp -H dotnet restore
sudo -u meierp ops/backup.sh /srv/backup/mei-erp
sudo -u meierp ops/deploy.sh
curl -fsS http://127.0.0.1:5090/health/ready && echo
```

`deploy.sh` publishes a new immutable release, switches the `current` symlink,
restarts the service, and automatically rolls back if readiness does not return.
For a manual code rollback, run `sudo -u meierp ops/rollback.sh`. Database
migrations are forward-only, so restore the pre-update PostgreSQL dump if a
schema change is not backward compatible.

## 6. Useful Proxmox and application checks

On the Proxmox host:

```bash
pct status 120
pct exec 120 -- systemctl --no-pager --full status mei-erp
pct exec 120 -- curl -fsS http://127.0.0.1:5090/health/ready
```

Inside the container:

```bash
systemctl status mei-erp mei-erp-monitor.timer --no-pager
journalctl -u mei-erp -n 200 --no-pager
tail -f /opt/mei-erp/current/logs/mei-erp-*.log
nginx -t
```

For day-two operations and incident handling, follow [`ops/RUNBOOK.md`](ops/RUNBOOK.md).
