import { defineConfig } from "vitest/config";
import path from "path";

export default defineConfig({
  // Use esbuild's automatic JSX runtime (matching tsconfig "jsx": "react-jsx")
  // so test files can use JSX without importing React, while keeping esbuild as
  // the transformer the v8 coverage provider understands.
  esbuild: {
    jsx: "automatic",
    jsxImportSource: "react",
  },
  resolve: {
    alias: {
      // Stub the design-system package in tests. Its barrel eagerly imports
      // framer-motion, whose motion-utils reads `process.env.NODE_ENV` at load
      // time and crashes under jsdom. The subpath aliases must come before the
      // bare specifier so "/hooks" and "/providers" match exactly instead of
      // being caught as a prefix of the bare alias.
      "@seliseblocks/genesis-os/hooks": path.resolve(
        __dirname,
        "./app/test-utils/stubs/blocks-kit.tsx",
      ),
      "@seliseblocks/genesis-os/providers": path.resolve(
        __dirname,
        "./app/test-utils/stubs/blocks-kit.tsx",
      ),
      "@seliseblocks/genesis-os": path.resolve(
        __dirname,
        "./app/test-utils/stubs/blocks-kit.tsx",
      ),
      "@": path.resolve(__dirname, "./app"),
      "@blocks-idp": path.resolve(__dirname, "./app/idp"),
      "@blocks-lmt": path.resolve(__dirname, "./app/cross-modules/lmt"),
      "@blocks-storage": path.resolve(__dirname, "./app/cross-modules/storage"),
      "@blocks-communication": path.resolve(
        __dirname,
        "./app/cross-modules/communication",
      ),
      "@blocks-identifier": path.resolve(
        __dirname,
        "./app/cross-modules/identifier",
      ),
      "@blocks-localization": path.resolve(
        __dirname,
        "./app/cross-modules/localization",
      ),
      "@blocks-utilities": path.resolve(
        __dirname,
        "./app/cross-modules/utilities",
      ),
      "@blocks-ai": path.resolve(__dirname, "./app/cross-modules/ai"),
      "@blocks-workflow": path.resolve(__dirname, "./app/modules/workflow"),
    },
  },
  test: {
    environment: "jsdom",
    globals: false,
    include: ["app/**/*.test.{ts,tsx}"],
    exclude: ["node_modules", "dist", "**/__mocks__/**"],
    setupFiles: ["app/test-setup.ts"],
    css: false,
    alias: {
      // Stub the design-system package in tests. Its barrel eagerly imports
      // framer-motion, whose motion-utils reads `process.env.NODE_ENV` at load
      // time and crashes under jsdom.
      "@seliseblocks/genesis-os": path.resolve(
        __dirname,
        "./app/test-utils/stubs/blocks-kit.tsx",
      ),
      "@seliseblocks/genesis-os/hooks": path.resolve(
        __dirname,
        "./app/test-utils/stubs/blocks-kit.tsx",
      ),
      "@seliseblocks/genesis-os/providers": path.resolve(
        __dirname,
        "./app/test-utils/stubs/blocks-kit.tsx",
      ),
    },
    coverage: {
      all: true,
      provider: "v8",
      reporter: ["text", "json-summary", "json", "clover"],
      include: ["app/**/*.{ts,tsx}"],
      exclude: [
        "app/**/*.test.*",
        "app/**/*.spec.*",
        "app/**/*.d.ts",
        "app/**/main.tsx",
        "app/**/vite-env.d.ts",
        "app/test-setup.ts",
        "app/**/test-utils/**",
        "app/**/__mocks__/**",
        "**/components/ui/**",
        "**/components/ui-kits/**",
        "app/**/*.stories.*",
        "**/__generated__/**",
        "**/*.gen.*",
      ],
    },
  },
});
