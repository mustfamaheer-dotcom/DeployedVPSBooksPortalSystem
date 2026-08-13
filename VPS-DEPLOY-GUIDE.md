# VPS Deployment Guide — New Project + Subdomain

Practical step-by-step guide to host a new project on the production VPS and attach a new subdomain to it. Based on the live environment (Coolify 4.1.2 on Hostinger VPS).

---

## 1. Environment Reference

| Item | Value |
|---|---|
| VPS (Hostinger) | `186.240.151.209` — SSH as `root` (key: `C:\Users\Moustafa Maher\.ssh\opencode_deploy`) |
| Deployment panel | **Coolify 4.1.2** (self-hosted PaaS) — UI at `http://186.240.151.209:8000` |
| Proxy | Coolify-managed **Traefik** (ports 80/443) — auto HTTPS via Let's Encrypt |
| Build source | **GitHub** — Coolify "GitHub App" integration already installed and connected to the accounts `mustfamaheer-dotcom` and `thenurdz26-cpu` |
| Domain | `thenurdz.online` (DNS managed in the Hostinger hPanel account that **owns** the domain — note: the Hostinger API account connected to automation does NOT own it) |
| Existing Coolify projects | `2` = "The Nurdz", `3` = "Books Portal" |
| Database server | Shared **MSSQL** container `mssql-s7668obn9cv81wuby75qvn9u-182459414945` (defined as a docker-compose app `s7668obn9cv81wuby75qvn9u` / "booksportal-mssql" in Coolify) |

Existing apps (reference for naming/pattern):

| App UUID | Name | Domain |
|---|---|---|
| `jil7z2en01bipyhwrq5ojw6c` | booksportal | `https://books-portal.thenurdz.online` |
| `kxz0bse2sy2q7fccwsyo59t0` | the-nurdz-student-panel | `https://nurdzstudent.thenurdz.online` |
| `y23ms5d4xmo2otr7863jqqbh` | the-nurdz-teacher-panel | `https://nurdzteacher.thenurdz.online` |
| `s7668obn9cv81wuby75qvn9u` | booksportal-mssql | — (docker-compose, the DB server) |

Workflow: **GitHub push → Coolify build (Dockerfile/Nixpacks) → Traefik HTTPS routing → subdomain live.**

---

## 2. Part 1 — Create the Subdomain (DNS)

The subdomain is just a DNS record pointing to the VPS IP. The application itself is created in Part 2.

### 2.1 Via hPanel (recommended)
1. Log in to the Hostinger hPanel account that owns `thenurdz.online`.
2. **Domains → thenurdz.online → DNS / Nameservers → DNS Zone Editor**.
3. Add record:
   - Type: `A`
   - Name: `yourproject` (creates `yourproject.thenurdz.online`)
   - Content: `186.240.151.209`
   - TTL: `14400` (default is fine)
4. Save. Propagation is usually < 5 minutes.

**Wildcard option (if you add many subdomains):** a single `A` record with name `*` → `186.240.151.209` covers every subdomain at once (`foo.thenurdz.online`, `bar.thenurdz.online`, …). Recommended if the project is not the only one that will be deployed.

### 2.2 Via Hostinger API (scriptable)
```bash
# NOTE: use the API token of the account that OWNS thenurdz.online
curl -X POST "https://developers.hostinger.com/api/dns/v1/domains/thenurdz.online/records" \
  -H "Authorization: Bearer <API_TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{"name":"yourproject","type":"A","content":"186.240.151.209","ttl":14400}'
```

### 2.3 Verify
```bash
nslookup yourproject.thenurdz.online        # must return 186.240.151.209
# or: Resolve-DnsName yourproject.thenurdz.online   (PowerShell on Windows)
```
Skip waiting — Coolify/Traefik will pick it up once DNS answers; HTTPS certificate is issued automatically on first request.

---

## 3. Part 2 — Create & Deploy the Project in Coolify (UI)

1. Open `http://186.240.151.209:8000` and log in.
2. **Projects** → open an existing project (e.g. "Books Portal") **or** click **+ New Project** for a separate one.
3. Click **+ New → Application**.
4. **Connect GitHub repository** — pick the GitHub App (already installed). Choose the repo and branch (`main`).
5. Fill in the application settings:
   - **Name**: e.g. `myproject` (short, lowercase)
   - **Domains**: `https://yourproject.thenurdz.online`  ← this is the subdomain from Part 1. Coolify generates the Traefik routing + Let's Encrypt cert automatically.
   - **Build Pack**: `Dockerfile` (repo root) — or `Nixpacks` for auto-detected languages (Node/Python/etc.), `Static` for plain HTML/JS.
   - **Base Directory**: set if the app lives in a subfolder of the repo (e.g. `web/`).
   - **Ports Exposes**: the container port your app listens on (e.g. `8080`).
   - **Environment Variables**: add `ConnectionStrings__...`, secrets, etc. — same pattern as booksportal (`ConnectionStrings__DefaultConnection`).
   - Optional: **Health Check** path (e.g. `/health`) so Coolify restarts unhealthy containers.
6. Click **Deploy**. First build pulls the repo, runs the build, and starts the container. Watch **Logs**.
7. Container is created as `<app-uuid>-<random-suffix>` (e.g. `jil7z2en01bipyhwrq5ojw6c-155907059326`). The container is automatically attached to the Traefik network — no manual network work for new apps.

Result: `https://yourproject.thenurdz.online` is live with automatic HTTPS.

---

## 4. Part 3 — Deploy via API (scripted / CI)

Usable when you need to redeploy from the CLI (this is how booksportal is redeployed).

1. Create an API token: Coolify → **Settings → API Tokens → Create** (e.g. name `opencode-deploy`). Copy it — it is shown once.
2. Trigger deploy of an app by its **UUID** (find it in the app's General settings, or in the DB — see §6):
```bash
curl -X POST "http://localhost:8000/api/v1/deploy?uuid=<APP_UUID>" \
  -H "Authorization: Bearer <API_TOKEN>"
```
(From your Windows machine use `http://186.240.151.209:8000/api/v1/deploy?uuid=...`)
3. The response contains a deployment UUID — poll until finished:
```bash
curl -s "http://localhost:8000/api/v1/deployments/<DEPLOYMENT_UUID>" \
  -H "Authorization: Bearer <API_TOKEN>"
```
4. Confirm the new container is up:
```bash
ssh root@186.240.151.209 "docker ps --format '{{.Names}} | {{.Image}} | {{.Status}}' | grep <APP_UUID>"
```

---

## 5. Part 4 — Post-Deploy Checks

```bash
curl -sI https://yourproject.thenurdz.online        # expect HTTP 200 + HTTPS
curl -sk https://localhost/yourproject -H "Host: yourproject.thenurdz.online"  # via traefik
ssh root@186.240.151.209 "docker logs <container> --tail 100"
```

Common issues:
- **502/504 on first request** — certificate still being issued; wait a minute and retry.
- **Wrong port** — app container port doesn't match **Ports Exposes** in Coolify.
- **Large uploads fail exactly at ~60 s** — see §7 (Traefik timeout). Already fixed globally; only comes back if proxy settings are re-saved in the UI.

---

## 6. Part 5 — Database for the New Project

The shared MSSQL server (`booksportal-mssql` container) hosts all project DBs. Create a dedicated database:

```bash
# SSH to the VPS
SQLC=$(docker ps --format '{{.Names}}' | grep '^mssql-')
PW=$(docker exec "$SQLC" env | grep '^MSSQL_SA_PASSWORD=' | cut -d= -f2-)
docker exec -i "$SQLC" sh -lc "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P \"$PW\" -C -d master -Q \"CREATE DATABASE MyProjectDB;\"" 
```

Then create the app login (or reuse the pattern from `PrintingBooksPortal` — user `booksportal_app`):
```bash
docker exec -i "$SQLC" sh -lc "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P \"$PW\" -C -d master -Q \"CREATE LOGIN myproj_app WITH PASSWORD='<StrongPassword>'; CREATE USER myproj_app FOR LOGIN myproj_app; ALTER ROLE db_owner ADD MEMBER myproj_app;\"" 
```

Wire it into the app via an env var in Coolify:
```
ConnectionStrings__DefaultConnection=Server=172.16.2.3,1433;Database=MyProjectDB;User Id=myproj_app;Password=<StrongPassword>;TrustServerCertificate=True;MultipleActiveResultSets=True;Connection Timeout=30
```
(`172.16.2.3` is the MSSQL container IP on the Docker network — verify with `docker inspect mssql-<...> --format '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}'`; the Docker network name is `s7668obn9cv81wuby75qvn9u`.)

---

## 7. Important Caveats

1. **Traefik 60-second upload timeout (already fixed, can come back):**
   Traefik v3.6 defaults to a 60 s read timeout which kills large uploads (502). The live proxy already has the fix:
   `--entrypoints.http.transport.respondingtimeouts.readtimeout=0` and `--entrypoints.https.transport.respondingtimeouts.readtimeout=0` in `/data/coolify/proxy/docker-compose.yml` (backups `.bak`, `.bak2`).
   If someone **saves proxy settings in the Coolify UI**, that file is regenerated and the fix is lost — reapply the two flags, then:
   ```bash
   cd /data/coolify/proxy && docker compose -f docker-compose.yml up -d
   docker network connect s7668obn9cv81wuby75qvn9u coolify-proxy   # extra network not in compose file
   ```
2. **GitHub is the single source of truth** — always `git push origin <branch>` (to GitHub) before deploying; Coolify builds from GitHub, not from local files.
3. **Sessions/data persistence** — the booksportal app keeps data on named volumes (`jil7z2en01bipyhwrq5ojw6c-appdata`). For a new app, create a volume for any stateful folder (e.g. uploads) in Coolify app settings so data survives redeploys.
4. **SQL access from the app** — MSSQL listens inside the Docker network; it is NOT exposed on the public IP. Do not change that.
5. **Sanity SQL on the MSSQL instance** (for reference):
   ```bash
   docker exec -i "$SQLC" sh -lc "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P \"$PW\" -C -d PrintingBooksPortal -Q 'SELECT COUNT(*) FROM AspNetUsers;'"
   ```

---

## 8. Quick Checklist (new project)

- [ ] DNS: `A yourproject.thenurdz.online → 186.240.151.209` added in hPanel (or wildcard `*`)
- [ ] `nslookup yourproject.thenurdz.online` returns the VPS IP
- [ ] Code pushed to GitHub (`git push origin main`)
- [ ] Coolify: Application created — repo, branch, base dir, build pack, port, `https://yourproject.thenurdz.online`
- [ ] Env vars set (DB connection string, secrets)
- [ ] Deploy → container running → `https://yourproject.thenurdz.online` answers 200
- [ ] Database created + user granted (if app needs one)
- [ ] Health check path set (if possible)
