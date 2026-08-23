# Blocks Logic — End-to-End Tests (Playwright)

E2E tests that drive the real app through the browser, including the dev-iam
login redirect flow. Workflow tests follow the shared Blocks E2E pattern
(documented in `e2e_monitor/BLOCKS-E2E-SPEC.md`).

## One-time setup

1. **Configure env**: copy the template and fill in your values:
   ```bash
   cd e2e
   cp .env.e2e.example .env.e2e
   ```
   Set `E2E_USERNAME` / `E2E_PASSWORD`. `.env.e2e` is gitignored; never commit
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
npm test              # setup + all workflow specs + teardown
npm run test:features # ordered subset from features.mjs
```

### Against remote dev (default)

The shipped `.env.e2e.example` targets the deployed dev host and sets
`E2E_NO_WEBSERVER=1`, so nothing is built or started locally:

```
E2E_BASE_URL=https://dev-logic.blocksdevelopers.com
E2E_NO_WEBSERVER=1
```

Reuse an existing project (recommended when console slots are limited):

```
E2E_REUSE_PROJECT_NAME=test
# or
E2E_PROJECT_ID=effa326b-8188-4aad-85e3-6e9a4d890c09
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

Auto-start runs `bash run.sh -b`, so **Git Bash's `bash` must be on PATH**. To
manage the server yourself, set `E2E_NO_WEBSERVER=1`.

### Other run modes

```bash
npm run test:headed   # watch it in a real browser
npm run test:ui       # Playwright UI mode
npm run report        # open the last HTML report
E2E_FEATURES=create npm run test:features   # single feature
```

## Knobs in `.env.e2e`

| Variable | Effect |
|---|---|
| `E2E_BASE_URL` | Logic app host under test. No default; missing value fails loudly. |
| `E2E_OS_BASE_URL` | OS host for project delete (defaults: dev-logic → dev-os). |
| `E2E_USERNAME` / `E2E_PASSWORD` | Dev-IAM test account. |
| `E2E_REUSE_PROJECT_NAME` | Reuse named project instead of creating `Test Project *`. |
| `E2E_PROJECT_ID` | Open project by UUID — skips console card search. |
| `E2E_KEEP_PROJECT=1` | Never delete shared project after run. |
| `E2E_NO_WEBSERVER=1` | Don't auto-start the app (required for remote dev). |
| `E2E_FEATURES` | Comma-separated feature ids or `all` for `test:features`. |
| `E2E_PAUSE_MS` | Hold browser after each test (headed debugging). |
| `E2E_SLOWMO` | Slow motion ms per Playwright action. |

## Layout

```
e2e/
  features.mjs                    # ordered workflow feature list
  run-e2e.mjs                     # sequential feature runner
  tests/
    auth/login.spec.ts            # standalone auth smoke test
    workflow/
      workflow.setup.spec.ts      # login + shared project + seed
      workflow.teardown.spec.ts   # OS delete when all passed
      *-workflow.spec.ts          # feature specs
  support/
    env.ts                        # E2E_BASE_URL, E2E_OS_BASE_URL, credentials
    login-helper.ts               # OIDC / dev-iam flow
    test-base.ts                  # shared test + failure tracking
    run-outcome.ts                # pass/fail → delete or keep project
    workflow-project.ts           # fixture read/write
    create-and-delete-project.ts  # console nav, reuse/create, OS delete
    workflow-helpers.ts           # openWorkflowList, ensureWorkflowExists
  fixtures/                       # gitignored runtime artifacts
```

## Lifecycle

1. **Setup** — OIDC login, reuse or create one shared project, navigate to Workflow, seed data if empty, write fixture + session.
2. **Features** — each spec opens Workflow via fixture URL; failures keep the project for debugging.
3. **Teardown** — delete project on **Blocks OS** only when every feature test passed.

See `e2e_monitor/BLOCKS-E2E-SPEC.md` for the full integration spec.
