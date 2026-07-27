# Blocks Logic — End-to-End Tests (Playwright)

E2E tests that drive the real app through the browser, including the dev-iam
login redirect flow.

## One-time setup

1. **Configure env** — copy the template and fill in your values:
   ```bash
   cd e2e
   cp .env.e2e.example .env.e2e
   ```
   Set `E2E_USERNAME` / `E2E_PASSWORD`. `.env.e2e` is gitignored — never commit
   real credentials.

2. **Install** Playwright + the browser:
   ```bash
   cd e2e
   npm install
   npx playwright install chromium
   ```

## Run

From the repo root:

```bash
./run.sh -te          # or: .\run.ps1 -te
```

or directly:

```bash
cd e2e
npm test
```

### Against remote dev (default)

The shipped `.env.e2e.example` targets the deployed dev host and sets
`E2E_NO_WEBSERVER=1`, so nothing is built or started locally:

```
E2E_BASE_URL=https://dev-logic.blocksdevelopers.com
E2E_NO_WEBSERVER=1
```

`global-setup.ts` prints this warning on that path — it is expected and
harmless, because the remote host already serves its own correct base URL:

```
[e2e] index.html not found ... — skipping BLOCKS_LOGIC_BASE_URL patch.
```

### Against a local build

Build the FE into `server/Api/wwwroot` and let Playwright start the API on
`API_PORT` (**5000**, see `run.sh`):

```
E2E_BASE_URL=https://dev-logic.blocksdevelopers.com:5000
# E2E_NO_WEBSERVER left unset / not 1
```

This needs a hosts entry pointing the domain at your machine:

```
127.0.0.1 dev-logic.blocksdevelopers.com
```

HTTPS on that port is opt-in and comes from the machine env vars **`LOGIC_SSL_CERT`**
and **`LOGIC_SSL_KEY`** (`run.sh` → `configure_backend_tls`). Both must be set and
both files must exist, otherwise the API falls back to plain HTTP on the same
port and `E2E_BASE_URL` must use `http://`.

Auto-start runs `bash run.sh -b`, so **Git Bash's `bash` must be on PATH**. To
manage the server yourself, set `E2E_NO_WEBSERVER=1`.

### Other run modes
```bash
npm run test:headed   # watch it in a real browser
npm run test:ui       # Playwright UI mode
npm run report        # open the last HTML report
```

## Knobs in `.env.e2e`

| Variable | Effect |
|---|---|
| `E2E_BASE_URL` | Host under test. No default — a missing value fails loudly. |
| `E2E_USERNAME` / `E2E_PASSWORD` | Dev-IAM test account (captcha is disabled on dev). |
| `E2E_NO_WEBSERVER=1` | Don't auto-start the app; you manage the server (required for remote dev). |
| `E2E_PAUSE_MS` | How long the browser holds after **each** test. Defaults to **10 s in headed mode**, 0 when headless; `0` disables. |
| `E2E_SLOWMO` | Milliseconds of delay per action, to watch the steps themselves. |
| `E2E_HOLD_MS` | Extra hold at the end of the login spec only. |

## Discovering / updating selectors

The username/password fields live on the dev-iam page. To capture or verify
selectors against the live page:

```bash
npm run codegen -- <E2E_BASE_URL>/login
```

## Layout

```
e2e/
  tests/auth/login.spec.ts   # login through dev-iam -> /app/console
  support/test-base.ts       # shared `test` with the post-test pause
  fixtures/                  # auth storage state (gitignored)
  playwright.config.ts       # baseURL + creds from .env.e2e
  global-setup.ts            # local-build index.html base-URL patch
```

The login spec saves its session to `fixtures/auth.json`; the `chromium`
project reuses it, so any spec added outside `tests/auth/` starts already
logged in.
