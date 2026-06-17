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

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryProvider>
      <NuqsAdapter>
        <TooltipProvider>
          <BlocksAppLayout
            config={{
              userBaseUrlKey: "BLOCKS_IAM_BASE_URL",
              projectBaseUrlKey: "BLOCKS_API_BASE_URL",
              name: "blocks-logic"
            }}
          >
            <RouterProvider router={router} />
          </BlocksAppLayout>
          <Toaster />
        </TooltipProvider>
      </NuqsAdapter>
    </QueryProvider>
  </StrictMode>,
);
