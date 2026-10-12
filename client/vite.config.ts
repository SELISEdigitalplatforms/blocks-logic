import react from "@vitejs/plugin-react";
import path from "path";
import { defineConfig, loadEnv, type Plugin } from "vite";
import fs from "fs";

// Monaco's AMD build, served from our own origin at /monaco/vs (see app/lib/monaco-loader.ts) so
// the CSP needs no CDN. Copied on build; served straight from node_modules in dev.
const MONACO_VS_DIR = path.resolve(__dirname, "node_modules/monaco-editor/min/vs");
const MONACO_VS_URL = "/monaco/vs";

function selfHostMonaco(): Plugin {
  let outDir = "";
  return {
    name: "self-host-monaco",
    configResolved(config) {
      outDir = path.resolve(config.root, config.build.outDir);
    },
    configureServer(server) {
      server.middlewares.use(MONACO_VS_URL, (req, res, next) => {
        const relative = decodeURIComponent((req.url ?? "/").split("?")[0]);
        const file = path.resolve(MONACO_VS_DIR, "." + relative);
        // Never serve outside the Monaco folder (e.g. /monaco/vs/../../package.json).
        if (!file.startsWith(MONACO_VS_DIR + path.sep) || !fs.existsSync(file) || !fs.statSync(file).isFile()) {
          return next();
        }
        const types: Record<string, string> = {
          ".js": "text/javascript",
          ".css": "text/css",
          ".ttf": "font/ttf",
          ".json": "application/json",
        };
        res.setHeader("Content-Type", types[path.extname(file)] ?? "application/octet-stream");
        fs.createReadStream(file).pipe(res);
      });
    },
    closeBundle() {
      // Fail the build rather than ship a blank code editor.
      if (!fs.existsSync(path.join(MONACO_VS_DIR, "loader.js"))) {
        throw new Error(`[self-host-monaco] ${MONACO_VS_DIR}/loader.js not found; run npm ci`);
      }
      fs.cpSync(MONACO_VS_DIR, path.join(outDir, MONACO_VS_URL), { recursive: true });
    },
  };
}

// HTTPS is driven solely by the machine env vars LOGIC_SSL_CERT / LOGIC_SSL_KEY.
// If either is unset/empty, or the file it points to is missing, fall back to HTTP (no throw).
function getHttpsConfig(): false | { key: Buffer; cert: Buffer } {
  const certPath = process.env.LOGIC_SSL_CERT;
  const keyPath = process.env.LOGIC_SSL_KEY;

  if (!certPath || !keyPath) {
    console.warn(
      "[vite] LOGIC_SSL_CERT / LOGIC_SSL_KEY not set — serving over HTTP.",
    );
    return false;
  }

  const resolvedCertPath = path.resolve(__dirname, certPath);
  const resolvedKeyPath = path.resolve(__dirname, keyPath);

  if (!fs.existsSync(resolvedCertPath) || !fs.existsSync(resolvedKeyPath)) {
    console.warn(
      `[vite] SSL cert files not found (cert: ${resolvedCertPath}, key: ${resolvedKeyPath}) — serving over HTTP.`,
    );
    return false;
  }

  return {
    key: fs.readFileSync(resolvedKeyPath),
    cert: fs.readFileSync(resolvedCertPath),
  };
}

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, __dirname, "BLOCKS_");
  const proxyTarget = env.BLOCKS_API_BASE_URL;
  const httpsConfig = getHttpsConfig();
  getHttpsConfig();

  return {
    envPrefix: ["BLOCKS_"],
    plugins: [react(), selfHostMonaco()],
    resolve: {
      alias: {
        "@": path.resolve(__dirname, "./app"),
        "@blocks-idp": path.resolve(__dirname, "./app/idp"),
        "@blocks-lmt": path.resolve(__dirname, "./app/cross-modules/lmt"),
        "@blocks-storage": path.resolve(__dirname, "./app/cross-modules/storage"),
        "@blocks-communication": path.resolve(__dirname, "./app/cross-modules/communication"),
        "@blocks-identifier": path.resolve(__dirname, "./app/cross-modules/identifier"),
        "@blocks-localization": path.resolve(__dirname, "./app/cross-modules/localization"),
        "@blocks-utilities": path.resolve(__dirname, "./app/cross-modules/utilities"),
        "@blocks-ai": path.resolve(__dirname, "./app/cross-modules/ai"),
        "@blocks-workflow": path.resolve(__dirname, "./app/modules/workflow"),
        "@blocks-functions": path.resolve(__dirname, "./app/modules/functions"),
      },
    },
    build: {
      outDir: "../server/Api/wwwroot",
      emptyOutDir: true,
    },
    server: {
      host: true, // Listen on all addresses (0.0.0.0)
      port: 4000,
      https: httpsConfig || undefined,
      allowedHosts: [
        "dev-cloud.blocksdevelopers.com",
        "localhost",
        ".seliseblocks.com",
        ".blocksdevelopers.com",
      ],
      proxy: {
          "/dev-idp-proxy": {
            target: "https://dev-idp.blocksdevelopers.com",
            changeOrigin: true,
            secure: true,
            rewrite: (path) => path.replace(/^\/dev-idp-proxy/, ""),
          },
          ...(proxyTarget ? {
            "/api": { 
              target: proxyTarget, 
              changeOrigin: true, 
              secure: false,
            },
            "/cloudbuild": {
              target: proxyTarget,
              changeOrigin: true,
              secure: false,
            },
            "/idp": { 
              target: proxyTarget, 
              changeOrigin: true, 
              secure: false,
            },
            "/identifier": { 
              target: proxyTarget, 
              changeOrigin: true, 
              secure: false,
            },
            "/communication": { 
              target: proxyTarget, 
              changeOrigin: true, 
              secure: false,
            },
            "/cloudconfiguration": { 
              target: proxyTarget, 
              changeOrigin: true, 
              secure: false,
            },
            "/uilm": { target: proxyTarget, changeOrigin: true, secure: false },
            "/utilities": { target: proxyTarget, changeOrigin: true, secure: false },
            "/lmt": { target: proxyTarget, changeOrigin: true, secure: false },
            "/mfa": { target: proxyTarget, changeOrigin: true, secure: false },
            "/alert": { target: proxyTarget, changeOrigin: true, secure: false },
            "/blocksai-api": { target: proxyTarget, changeOrigin: true, secure: false },
            "/studio": { target: proxyTarget, changeOrigin: true, secure: false },
            "/uds": { target: proxyTarget, changeOrigin: true, secure: false },
          } : {}),
        },
    },
  };
});
