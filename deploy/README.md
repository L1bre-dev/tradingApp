# Installing L1bre Journal on the VPS

Written for the OVH VPS-2-2026 with **Ubuntu 26.04 LTS** and the domain **fta.trading**.
You do this once. Allow about 30 minutes. Commands are run on the VPS over SSH unless said otherwise.

What you end up with: three Docker containers (Caddy for HTTPS, the app, PostgreSQL) in `/opt/journal`,
a nightly database backup, and the app at https://fta.trading.

---

## 0. Before you start

1. **DNS**: at your domain registrar, create an **A record** `fta.trading` → the VPS IPv4 address
   (and an **AAAA** record → its IPv6 address if the VPS has one). It can take a few minutes to an hour to propagate.
   Check from your computer: `ping fta.trading` must answer with the VPS address.
2. **The app image must exist**: the first PR must be merged, and the **CI** run on `main` must be green
   (GitHub → tradingApp → Actions). That run publishes the image `ghcr.io/l1bre-dev/tradingapp`.
3. **A GitHub token to download the image** (the image is private, like the repository):
   GitHub → your avatar → Settings → Developer settings → Personal access tokens → **Tokens (classic)** →
   Generate new token (classic). Name it `VPS pull`, choose an expiration, tick **only `read:packages`**, generate,
   and keep the token for step 3. (GitHub's container registry only accepts classic tokens.)

## 1. Connect and update

From your computer (OVH emails you the user name, usually `ubuntu`):

```bash
ssh ubuntu@fta.trading
```

On the VPS:

```bash
sudo apt update && sudo apt full-upgrade -y
sudo reboot        # only if the upgrade asks for it; reconnect afterwards
```

**SSH key only**: if you log in with an SSH key (recommended), you can disable password logins:
`sudo nano /etc/ssh/sshd_config.d/50-no-password.conf`, write `PasswordAuthentication no`, save, then
`sudo systemctl restart ssh`. **Test a new SSH connection in a second terminal before closing the first one.**

## 2. Firewall and Docker

Firewall: allow only SSH and the web.

```bash
sudo ufw allow OpenSSH
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw allow 443/udp
sudo ufw enable
```

Note: ports published by Docker bypass ufw. That is fine here because only Caddy publishes ports (80 and 443),
and PostgreSQL is never published.

Docker, from the official instructions (https://docs.docker.com/engine/install/ubuntu/):

```bash
sudo apt install -y ca-certificates curl
sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
sudo chmod a+r /etc/apt/keyrings/docker.asc
sudo tee /etc/apt/sources.list.d/docker.sources <<EOF
Types: deb
URIs: https://download.docker.com/linux/ubuntu
Suites: $(. /etc/os-release && echo "${UBUNTU_CODENAME:-$VERSION_CODENAME}")
Components: stable
Architectures: $(dpkg --print-architecture)
Signed-By: /etc/apt/keyrings/docker.asc
EOF
sudo apt update
sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo docker run --rm hello-world
```

Let your user run Docker without `sudo`, then **log out and back in** for it to apply:

```bash
sudo usermod -aG docker $USER
exit
```

## 3. Get the app files

```bash
ssh ubuntu@fta.trading
sudo mkdir -p /opt/journal && sudo chown $USER: /opt/journal && cd /opt/journal

# Log in to GitHub's registry with the token from step 0.3 (paste it when asked, it won't show)
read -s CR_PAT && echo "$CR_PAT" | docker login ghcr.io -u L1bre-dev --password-stdin && unset CR_PAT

# The deployment files are inside the image: extract them here
docker pull ghcr.io/l1bre-dev/tradingapp:latest
docker run --rm --entrypoint tar ghcr.io/l1bre-dev/tradingapp:latest -C /deploy -c . | tar -x
ls -a     # docker-compose.yml  Caddyfile  backup.sh  .env.example
```

## 4. Settings

```bash
cp .env.example .env
openssl rand -hex 24      # copy the result: it is the database password
nano .env
```

Fill in:

- `DOMAIN=fta.trading`
- `ADMIN_EMAIL=` your email (it becomes your login)
- `POSTGRES_PASSWORD=` the value generated above

Save (Ctrl+O, Enter, Ctrl+X), then protect the file: `chmod 600 .env`.

## 5. Start

```bash
docker compose up -d
docker compose ps         # caddy, app, postgres: "running" (postgres "healthy")
docker compose logs app | grep "FIRST ADMIN INVITATION"
```

The last command prints your personal invitation link, `https://fta.trading/Account/Invite?token=...`.
Open it, choose your name and password: you are the admin.

- The first visit can take a few seconds: Caddy is getting the HTTPS certificate.
- If the link is lost: `docker compose restart app` prints a new one (the old one stops working).
  This only happens while no admin account exists.

## 6. Nightly backup

```bash
cd /opt/journal && ./backup.sh          # test it once: "Backup written: backups/journal-....sql.gz"
crontab -e                              # choose nano if asked, then add this line at the end:
```

```
30 3 * * * /opt/journal/backup.sh >> /opt/journal/backups/backup.log 2>&1
```

Backups are kept 14 days in `/opt/journal/backups`. They are on the same server as the database, so
**copy them elsewhere from time to time**. From your computer:

```bash
scp 'ubuntu@fta.trading:/opt/journal/backups/*.sql.gz' .
```

(An automatic copy to OVH Object Storage can come later.)

---

## Everyday commands (in `/opt/journal`)

| What | Command |
|---|---|
| Update to the latest version (after a PR is merged and CI is green) | `docker compose pull && docker compose up -d` |
| Is everything running? | `docker compose ps` |
| App logs (last 200 lines) | `docker compose logs app --tail 200` |
| Restart the app | `docker compose restart app` |
| Go back to a previous version | set `APP_TAG=sha-<full commit id>` in `.env`, then `docker compose up -d` |
| Stop everything (data is kept) | `docker compose down` |

Never run `docker compose down -v`: `-v` deletes the database volume.

## Restoring a backup

This replaces the current database with the backup.

```bash
cd /opt/journal
docker compose stop app
docker compose exec -T postgres dropdb -U journal journal
docker compose exec -T postgres createdb -U journal journal
gunzip -c backups/journal-YYYYMMDD-HHMMSS.sql.gz | docker compose exec -T postgres psql -U journal -d journal -q -v ON_ERROR_STOP=1
docker compose start app
```
