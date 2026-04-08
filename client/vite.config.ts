import react from "@vitejs/plugin-react";
import path from "path";
import { defineConfig } from "vite";

export default defineConfig({
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
  },
});
