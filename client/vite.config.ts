import react from "@vitejs/plugin-react";
import path from "path";
import { defineConfig, loadEnv } from "vite";

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, __dirname, "BLOCKS_");
  const proxyTarget = env.BLOCKS_API_BASE_URL;

  return {
    envPrefix: ["BLOCKS_"],
    plugins: [react()],
    resolve: {
      alias: {
        "@": path.resolve(__dirname, "./app"),
        "@blocks-idp": path.resolve(__dirname, "./app/idp"),
        "@blocks-lmt": path.resolve(__dirname, "./app/cross-modules/lmt"),
        "@blocks-storage": path.resolve(__dirname, "./app/cross-modules/storage"),
        "@blocks-communication": path.resolve(__dirname, "./app/cross-modules/communication"),
        "@blocks-identifier": path.resolve(__dirname, "./app/cross-modules/identifier"),
        "@blocks-localization": path.resolve(__dirname, "./app/cross-modules/localization"),
      },
    },
    build: {
      outDir: "../server/Api/wwwroot",
      emptyOutDir: true,
    },
    server: {
      port: 4100,
      proxy: proxyTarget
        ? {
            "/idp": { target: proxyTarget, changeOrigin: true, secure: true },
            "/identifier": { target: proxyTarget, changeOrigin: true, secure: true },
            "/communication": { target: proxyTarget, changeOrigin: true, secure: true },
            "/cloudconfiguration": { target: proxyTarget, changeOrigin: true, secure: true },
            "/uilm": { target: proxyTarget, changeOrigin: true, secure: true },
            "/utilities": { target: proxyTarget, changeOrigin: true, secure: true },
            "/cloudbuild": { target: proxyTarget, changeOrigin: true, secure: true },
            "/lmt": { target: proxyTarget, changeOrigin: true, secure: true },
            "/mfa": { target: proxyTarget, changeOrigin: true, secure: true },
            "/alert": { target: proxyTarget, changeOrigin: true, secure: true },
            "/blocksai-api": { target: proxyTarget, changeOrigin: true, secure: true },
            "/studio": { target: proxyTarget, changeOrigin: true, secure: true },
            "/uds": { target: proxyTarget, changeOrigin: true, secure: true },
          }
        : undefined,
    },
  };
});
