import { createBrowserRouter, Navigate, Outlet } from "react-router";

import WorkflowsPage from "./routes/private/workflows/workflows-page";
import SchedulesPage from "./routes/private/schedules/schedules-page";
import ScheduleCreatePage from "./routes/private/schedules/schedule-create-page";
import ScheduleEditPage from "./routes/private/schedules/schedule-edit-page";
import ScheduleDetailsPage from "./routes/private/schedules/schedule-details-page";
import WorkflowDetailsPage from "./routes/private/workflow-details/workflow-details-page";
import FunctionsPage from "./routes/private/functions/functions-page";
import FunctionDetailsPage from "./routes/private/functions/function-details-page";
import ProxiesPage from "./routes/private/proxy/proxies-page";
import ProxyCreatePage from "./routes/private/proxy/proxy-create-page";
import ProxyEditPage from "./routes/private/proxy/proxy-edit-page";
import ProxyDetailsPage from "./routes/private/proxy/proxy-details-page";

import {
  AuthResolver,
  PublicGuard,
  LoginPage,
  ProtectedGuard,
  ConsoleLayout,
  ConsolePage,
  CallbackPage,
  ProfilePage,
  DashboardOverview,
  DashboardRoute,
} from "@seliseblocks/genesis-os";
import { navigationMenus } from "./constants/navigation-menus";

const redirectPaths: Record<string, string> = {
  "/workflow/*": "/app/workflow",
  "/schedules/*": "/app/schedule",
  "/schedule/*": "/app/schedule",
  "/functions/*": "/app/functions",
  "/proxy/*": "/app/proxy",
};



export const router = createBrowserRouter([
  {
    // Set User Auth Information and resolve authentication state before rendering any route
    element: (
      <AuthResolver>
        <Outlet />
      </AuthResolver>
    ),
    children: [
      {
        element: (
          <PublicGuard>
            <Outlet />
          </PublicGuard>
        ),
        children: [
          {
            path: "/login",
            children: [
              { index: true, element: <LoginPage /> },
              {
                path: "callback",
                element: <CallbackPage defaultRedirectUrl="/app/console" />,
              },
            ],
          },
        ],
      },

      // protected
      {
        path: "/app",
        element: (
          <ProtectedGuard>
            <Outlet />
          </ProtectedGuard>
        ),

        children: [
          { index: true, element: <Navigate to="/app/console" replace /> },
          {
            element: (
              <ConsoleLayout>
                <Outlet />
              </ConsoleLayout>
            ),
            children: [
              { path: "console", element: <ConsolePage /> },
              { path: "profile", element: <ProfilePage /> },
            ],
          },
          {
            // impersonate
            path: ":itemId",
            element: <DashboardRoute redirectPaths={redirectPaths} navigationMenus={navigationMenus} />,
            children: [
              { path: "dashboard", element: <DashboardOverview /> },
              { path: "workflow/:id", element: <WorkflowDetailsPage /> },
              { path: "workflow", element: <WorkflowsPage /> },
              { path: "schedule", element: <SchedulesPage /> },
              { path: "schedule/new", element: <ScheduleCreatePage /> },
              { path: "schedule/:scheduleId/edit", element: <ScheduleEditPage /> },
              { path: "schedule/:scheduleId", element: <ScheduleDetailsPage /> },
              { path: "functions", element: <FunctionsPage /> },
              { path: "functions/:functionId", element: <FunctionDetailsPage /> },
              { path: "proxy", element: <ProxiesPage /> },
              { path: "proxy/new", element: <ProxyCreatePage /> },
              { path: "proxy/:proxyId/edit", element: <ProxyEditPage /> },
              { path: "proxy/:proxyId", element: <ProxyDetailsPage /> },
              { path: "profile", element: <ProfilePage /> },


            ],


          },
        ],
      },
      { path: "/", element: <Navigate to="/app/console" replace /> },
      { path: "*", element: <Navigate to="/login" replace /> },
    ],
  },
]);
