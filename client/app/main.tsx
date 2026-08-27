import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { RouterProvider } from "react-router/dom";
import { NuqsAdapter } from "nuqs/adapters/react-router/v8";
import { Toaster } from "./components/ui-kits/toaster/toaster";
import QueryProvider from "./providers/query-provider";
import { router } from "./router";
import "./styles/globals.css";
import { TooltipProvider } from "./components/ui-kits/tooltip/tooltip";
import { BlocksAppLayout } from "@seliseblocks/genesis-os";
import { ThemeProvider } from "./hooks/use-theme";
import {
  attachQueryErrorReporting,
  getRollbar,
  RollbarProvider,
} from "@seliseblocks/genesis-os/observability";
import { SERVICE_NAME } from "./constants/service.constant";
import { getQueryClient } from "./providers/query-provider";

attachQueryErrorReporting(
  getQueryClient(),
  getRollbar({ service: SERVICE_NAME }),
);

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <RollbarProvider service={SERVICE_NAME}>
      <QueryProvider>
        <ThemeProvider>
          <NuqsAdapter>
            <TooltipProvider>
              <BlocksAppLayout
                config={{
                  name: SERVICE_NAME,
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
    </RollbarProvider>
  </StrictMode>,
);
