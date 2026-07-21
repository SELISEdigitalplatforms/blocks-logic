import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { RouterProvider } from "react-router-dom";
import { NuqsAdapter } from "nuqs/adapters/react-router/v6";
import { Toaster } from "./components/ui-kits/toaster/toaster";
import QueryProvider from "./providers/query-provider";
import { router } from "./router";
import "./styles/globals.css";
import { TooltipProvider } from "./components/ui-kits/tooltip/tooltip";
import { BlocksAppLayout } from "@seliseblocks/blocks-kit";
import { ThemeProvider } from "./hooks/use-theme";


createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryProvider>
      <ThemeProvider>
        <NuqsAdapter>
          <TooltipProvider>
            <BlocksAppLayout
              config={{
                name: "blocks-logic",
                appLogoUrl: {
                  dark: "/Logo_White.svg",
                  light: "/Logo.svg",
                },
              }}>
              <RouterProvider router={router} />
            </BlocksAppLayout>
            <Toaster />
          </TooltipProvider>
        </NuqsAdapter>
      </ThemeProvider>
    </QueryProvider>
  </StrictMode>,
);
