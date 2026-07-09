import { createBrowserRouter, Navigate, Outlet } from "react-router-dom";
// import { DashboardOverview } from "./pages/dashboard/dashboard-overview";

import WorkflowsPage from "./routes/private/workflows/workflows-page";
import WorkflowDetailsPage from "./routes/private/workflow-details/workflow-details-page";

import {
  AuthResolver,
  PublicGuard,
  LoginPage,
  ProtectedGuard,
  ConsoleLayout,
  ConsolePage,
  CallbackPage,
  ProfilePage,
  ProjectOverviewLayout,
  DashboardOverview,
  DashboardLayout,
  EnvironmentsPage,
  ProjectOverviewRoute,
  DashboardRoute,
} from "@seliseblocks/blocks-kit";
import { navigationMenus } from "./constants/navigation-menus";

const redirectPaths: Record<string, string> = {
  "/workflow/*": "/app/workflow",
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
            path: "project/:tenantGroupId",
            element: <ProjectOverviewRoute redirectPaths={redirectPaths} navigationMenus={navigationMenus} />,
            children: [{ path: "environments", element: <EnvironmentsPage /> }],
          },
          {
            // impersonate
            path: ":itemId",
            element: <DashboardRoute redirectPaths={redirectPaths} navigationMenus={navigationMenus} />,
            children: [
              { path: "dashboard", element: <DashboardOverview /> },
              { path: "workflow/:id", element: <WorkflowDetailsPage /> },
              { path: "workflow", element: <WorkflowsPage /> },
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
