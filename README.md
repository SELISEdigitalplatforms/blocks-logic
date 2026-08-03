# Blocks Logic

Blocks Logic is the workflow and automation service of the SELISE `<Blocks/>` platform: a **React** (Vite, TypeScript) single-page application with a visual workflow builder, a **Genesis-backed ASP.NET Core API**, and a **background Worker** that executes workflows and consumers. The SPA is built into `server/Api/wwwroot` so Kestrel can serve the UI and backend from one host. The logic domain (`server/DomainService`) covers workflow definition and execution (triggers, nodes, transforms), the template engine, magic links, PDF generation, sequences, geolocation and storage helpers.

## Project structure

```
blocks-logic/
├── client/                         # React + Vite + TypeScript
│   ├── app/                        # Application source
│   │   ├── modules/workflow/       # Visual workflow builder (React Flow canvas, nodes, triggers)
│   │   ├── routes/                 # Route modules
│   │   ├── components/, hooks/, lib/, providers/
│   │   ├── main.tsx, router.tsx
│   │   └── …
│   ├── public/                     # Static assets
│   ├── index.html
│   ├── vite.config.ts              # build.outDir → ../server/Api/wwwroot; `BLOCKS_*` env prefix
│   ├── package.json
│   └── .env.example                # Copy to .env (see below)
├── server/
│   ├── Api/                        # Web host (Kestrel, Genesis, controllers, static SPA)
│   │   ├── Controllers/            # HTTP API (attribute routes; `api` prefix via convention)
│   │   ├── wwwroot/                # Vite output (generated; do not edit by hand)
│   │   ├── Program.cs
│   │   ├── Api.csproj
│   │   └── GlobalApiRoutePrefixConvention.cs
│   ├── Worker/                     # Background worker (message consumers, workflow execution)
│   ├── DomainService/              # Logic domain: Workflow, TemplateEngine, MagicLink,
│   │                               #   PdfGenerator, Sequence, Geolocation, Messaging, Storage
│   ├── Authentication.DomainService/
│   ├── Captcha.DomainService/
│   ├── Cloud.DomainService/
│   ├── Cloud.LmtService/
│   ├── CloudConfiguration.DomainService/
│   ├── Iam.DomainService/
│   ├── Identifier.DomainService/
│   ├── Mail.DomainService/
│   ├── Mfa.DomainService/
│   ├── Notification.DomainService/
│   ├── XUnitTest/                  # Unit tests
│   ├── Captcha.Driver/, Iam.Driver/, Mfa.Driver/, Mail.Driver/   # Driver-style projects
│   └── Blocks.slnx                 # Solution: Api, domain libraries, Worker, XUnitTest
├── e2e/                            # Playwright end-to-end tests (see e2e/README.md)
├── scripts/                        # scan.sh and deploy.sh entry points
├── run.sh                          # Build/run helpers (Unix/macOS; see below)
├── run.ps1                         # Same role on Windows (PowerShell; see below)
├── LICENSE
└── README.md
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (`TargetFramework` is `net10.0` in `server/Directory.Build.props`)
- [Node.js LTS](https://nodejs.org/) (for `npm install`, `npm run dev`, `npm run build`)
- [Docker](https://docs.docker.com/get-docker/) with [Docker Compose](https://docs.docker.com/compose/) (for local backing services; see below)

### Local infrastructure

To run Blocks Logic **locally**, clone **[blocks-infra](https://github.com/SELISEdigitalplatforms/blocks-infra)** and bring up its **Docker Compose** stack (for example **`docker compose up -d`**, using the compose file and options described in that repository). Start that stack **before** or **alongside** the API and Worker from this repo so local dependencies (databases, queues, and so on) are available.

## How to run

Both **`run.sh`** (Bash) and **`run.ps1`** (PowerShell) live at the repo root. They **require an option**; calling them with no recognized flags prints usage and exits.

**Ports:** both scripts assume API port **5000**. `run.sh` also uses **4000** for `npm run dev` when you pass **`-f`** (see `run.sh`). `server/Api/Properties/launchSettings.json` may differ for IDE launches; use the URL your process prints.

### Options (same flags, different invocation)

| Option | Role |
|--------|------|
| **`-a`**, **`--all`** | Install/build client, sync **`client/dist`** → **`server/Api/wwwroot`**, then start **API** and **Worker** |
| **`-b`**, **`--backend`** | Run **API** only (`dotnet run` on `server/Api/Api.csproj`) |
| **`-w`**, **`--worker`** | Run **Worker** only (`server/Worker/Worker.csproj`) |
| **`-f`**, **`--frontend`** | Vite dev server: **`npm run dev`** in **`client/`** (`run.sh` installs dependencies only if **`node_modules`** is missing; **`run.ps1`** runs **`npm install`** every time) |
| **`-k`**, **`--kill-port`** | Stop whatever is listening on the API port (**5000**) |
| **`-n`**, **`--npm`** *args…* | Run **`npm`** in **`client/`** (e.g. `run test`, `run build`) |
| **`-tb`**, **`-tf`**, **`-te`**, **`-ta`** | Test shortcuts: backend, frontend, e2e, all (see `./run.sh -h`) |
| **`-h`**, **`--help`** | Show usage |

**Windows only (`run.ps1`):** **`-d`** / **`--dotnet`** *args…* runs **`dotnet`** from the repo root (for example restore, build, or test commands).

### Unix / macOS (`run.sh`)

```bash
./run.sh -a              # full stack (build + API + Worker in this shell)
./run.sh -b              # API only (frees port 5000 first)
./run.sh -f              # Vite dev server
./run.sh -n run test     # npm in client/
```

`run.sh` syncs the built SPA with **`rsync`** (`dist/` → `wwwroot/`). For **`-a`**, the API and Worker run as background jobs in the same terminal; **Ctrl+C** runs the script’s cleanup trap.

### Windows (`run.ps1`)

```powershell
.\run.ps1 -a             # build + start API and Worker in separate windows
.\run.ps1 -b             # API only (restore + free port 5000 first)
.\run.ps1 -f             # Vite dev server
.\run.ps1 -n run test    # npm in client/
.\run.ps1 -d test server/XUnitTest/XUnitTest.csproj
```

`run.ps1` syncs **`dist`** → **`wwwroot`** with **`robocopy`**. It runs **`dotnet restore`** on the Api and Worker projects before **`-b`**, **`-w`**, and **`-a`**. With **`-a`**, two extra PowerShell windows open (one for the API, one for the Worker); press **Enter** in the original window to stop those processes.

### Without the scripts

If the client is already built into **`wwwroot`**:

```bash
dotnet run --project server/Api/Api.csproj
```

### Client environment (`BLOCKS_*`)

Vite exposes env vars prefixed with **`BLOCKS_`** (see **`client/vite.config.ts`**). Copy **`client/.env.example`** → **`client/.env`** and set values as needed:

- **`BLOCKS_API_BASE_URL`**: base URL the client uses for API calls.
- **`BLOCKS_X_BLOCKS_KEY`**: Genesis / Blocks project key when your environment requires it.
- **`BLOCKS_IAM_BASE_URL`**: IAM base URL for the OIDC sign-in flow.
- **`BLOCKS_OIDC_CLIENT_ID`**: OIDC client id used by the sign-in flow.

Rebuild the client (`npm run build` in **`client/`** or **`./run.sh -a`** / **`.\run.ps1 -a`**) after changing env for **production** bundles.

## Production / publish

Build the client, then publish the API (ensure **`wwwroot`** contains the built SPA if you want the UI in the output):

```bash
(cd client && npm install && npm run build)
dotnet publish server/Api/Api.csproj -c Release -o ./publish
```

No Node process is required on the server at runtime.

## Tests

Run from the repository root:

```bash
# backend unit tests (xUnit)
dotnet test server/XUnitTest/XUnitTest.csproj

# frontend unit tests (Vitest)
npm --prefix client run test

# end-to-end tests (Playwright); needs a reachable app and e2e/.env.e2e,
# see e2e/README.md for setup and target modes
npm --prefix e2e run test
```

Coverage:

```bash
dotnet test server/XUnitTest/XUnitTest.csproj --collect:"XPlat Code Coverage"
npm --prefix client run test -- --coverage
```

## Scanning and deployment

- `scripts/scan.sh` is the security scan entry point (SAST, SCA and secret scanning). It is intentionally not tracked in git; internal environments provide it.
- `scripts/deploy.sh` is the maintainer deploy script for a systemd host: it checks out the latest `inception`, builds the client, publishes the Api and Worker projects, and installs and restarts their systemd services.

## API and routing

- Controllers live under **`server/Api/Controllers/`** (workflow, template, notification, mail, storage, people, project, language, monitor, migration, deployment, health, captcha, MFA). Route templates omit the **`api`** segment in code; **`GlobalApiRoutePrefixConvention`** in **`Program.cs`** adds the **`api`** prefix for attribute-routed controllers.
- **`/api` is reserved for the HTTP API** in the integrated setup; keep client-side routes from colliding with API paths.

## Contributing and security

- Contribution conventions and workflow: [CONTRIBUTING.md](CONTRIBUTING.md)
- Reporting a vulnerability: [SECURITY.md](SECURITY.md)
- Community standards: [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)

## License

See [LICENSE](LICENSE).
