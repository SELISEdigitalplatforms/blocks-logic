# Blocks Logic — End-to-End Tests (Playwright)

Follows the shared Blocks product e2e template
(`e2e-spec/SPEC-blocks-e2e-suite-template.md`),
same shape as `blocks-data/e2e`.

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
npm test              # logic-setup + feature specs + logic-teardown
npm run test:features # ordered subset from features.mjs
```

### Against remote dev (default)

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

```
E2E_BASE_URL=https://dev-logic.blocksdevelopers.com:5000
# E2E_NO_WEBSERVER left unset / not 1
```

Hosts entry:

```
127.0.0.1 dev-logic.blocksdevelopers.com
```

### Other run modes

```bash
npm run test:headed
npm run test:ui
npm run report
E2E_FEATURES=create npm run test:features
```

## Knobs in `.env.e2e`

| Variable | Effect |
|---|---|
| `E2E_BASE_URL` | Blocks **Logic** host. Dev: `https://dev-logic.blocksdevelopers.com`. Prod: `https://logic.seliseblocks.com`. |
| `E2E_OS_BASE_URL` | Blocks **OS** (optional). Derived: `dev-logic`→`dev-os`, `logic.`→`os.`. |
| `E2E_USERNAME` / `E2E_PASSWORD` | OIDC test account. |
| `PROJECT_NAME` | Optional create prefix (`${PROJECT_NAME} ${Date.now()}`). |
| `E2E_REUSE_PROJECT_NAME` | Reuse named project instead of creating. |
| `E2E_PROJECT_ID` | Open project by UUID — skips console card search. |
| `E2E_KEEP_PROJECT=1` | Never delete shared project after run. |
| `E2E_NO_WEBSERVER=1` | Don't auto-start the app (required for remote host). |
| `E2E_FEATURES` | Comma-separated feature ids or `all` for `test:features`. |
| `E2E_PAUSE_MS` | Hold browser after each test (headed debugging). |
| `E2E_SLOWMO` | Slow motion ms per Playwright action. |

## Lifecycle

Playwright projects: **`logic-setup` → `logic` → `logic-teardown`**

1. **Suite setup** (`tests/suite/suite.setup.spec.ts`) — OIDC login, save `logic-session.json`, reuse or create one shared project, write `logic-project.json`. Does **not** open Workflow.
2. **Features** (`tests/workflow/*.spec.ts`, …) — use session; open shared dashboard then feature area via helpers (`openWorkflowList` seeds Workflow if empty). Failures keep the project.
3. **Suite teardown** (`tests/suite/suite.teardown.spec.ts`) — delete project on **Blocks OS** only when every `logic` test passed (unless `E2E_KEEP_PROJECT=1`).

## Layout

```
e2e/
  features.mjs / run-e2e.mjs      # optional ordered feature runner
  tests/
    auth/login.spec.ts            # standalone auth smoke (project "setup")
    suite/
      suite.setup.spec.ts         # login + shared project
      suite.teardown.spec.ts      # OS delete when suite passed
    workflow/*.spec.ts            # feature specs only
  support/
    env.ts                        # Logic URL + OS derivation
    login-helper.ts
    create-and-delete-project.ts
    logic-project.ts              # logic-session / logic-project fixtures
    suite-helpers.ts              # openSharedProjectDashboard
    run-outcome.ts                # markSuiteTestFailed
    test-base.ts                  # pause + mark failures for project "logic"
    workflow-helpers.ts           # feature-only
  fixtures/                       # gitignored
  SPEC-multi-env.md
```

To stand up another Blocks product e2e, copy this layout and rename the product slug (`logic` → `{product}`) — see the template SPEC.
