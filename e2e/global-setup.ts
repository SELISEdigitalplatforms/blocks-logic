import fs from "fs";
import path from "path";

/**
 * Point a locally-served Blocks Logic at itself, not the remote dev host.
 *
 * The built index.html carries runtime config in `window.__BLOCKS_ENV__`. The
 * .NET host fills `__BLOCKS_LOGIC_BASE_URL__` from the `FrontendRuntime`
 * section (Program.cs), which on dev is the deployed host WITHOUT a port
 * (https://dev-logic.blocksdevelopers.com). When Logic runs locally on
 * API_PORT, the SPA would then send its API calls to the remote dev server, so
 * the UI shows no local data. This patches the served index.html so
 * BLOCKS_LOGIC_BASE_URL === E2E_BASE_URL.
 *
 * Idempotent and order-independent: it rewrites the concrete value (or the
 * `__BLOCKS_LOGIC_BASE_URL__` placeholder), so it holds whether it runs before
 * or after the host's own startup replacement. Because the command in
 * playwright.config.ts is `run.sh -b` (no FE rebuild), nothing overwrites it.
 *
 * On remote dev (E2E_NO_WEBSERVER=1) there is no local wwwroot/index.html, so
 * this logs the "skipping" warning below and does nothing — which is correct:
 * the remote host already serves its own correct base URL.
 */
export default function globalSetup() {
  const baseURL = process.env.E2E_BASE_URL;
  if (!baseURL) return; // playwright.config.ts already throws when unset

  const indexHtml = path.resolve(__dirname, "../server/Api/wwwroot/index.html");
  if (!fs.existsSync(indexHtml)) {
    console.warn(
      `[e2e] index.html not found at ${indexHtml} — skipping BLOCKS_LOGIC_BASE_URL patch. ` +
        `Build the FE first (cd client && npm run build, or run.sh -a).`,
    );
    return;
  }

  const original = fs.readFileSync(indexHtml, "utf8");
  const patched = original.replace(
    /(BLOCKS_LOGIC_BASE_URL:\s*")([^"]*)(")/g,
    `$1${baseURL}$3`,
  );

  if (patched === original) {
    console.log(`[e2e] BLOCKS_LOGIC_BASE_URL already "${baseURL}" — no patch needed.`);
    return;
  }

  fs.writeFileSync(indexHtml, patched);
  console.log(`[e2e] Patched BLOCKS_LOGIC_BASE_URL -> "${baseURL}" in served index.html.`);
}
