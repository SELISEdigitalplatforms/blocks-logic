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
  ImpersonationChecker,
  ImpersonationTerminator,
  ImpersonationSynchronizer,
  ConsolePage,
  CallbackPage,
  ProfilePage,
  ProjectOverviewLayout,
  DashboardOverview,
  DashboardLayout,
  EnvironmentsPage,
} from "@seliseblocks/blocks-kit";
import { navigationMenus } from "./constants/navigation-menus";

const redirectPaths: Record<string, string> = {
  "/workflow/*": "/app/workflow",
};

export const router = createBrowserRouter([
  {
    element: <Outlet />,
    children: [
      // All Redirect Url Handle here
      {
        element: <Outlet />,
        children: [
          {
            path: "/login/callback",
            element: <CallbackPage defaultRedirectUrl="/app/console" />,
          },
        ],
      },
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
              { path: "/login", element: <LoginPage/> },
            ],
          },

          // protected
          {
            element: (
              <ProtectedGuard>
                <Outlet />
              </ProtectedGuard>
            ),
            children: [
              {
                element: (
                  <Outlet />
                ),
                children: [
                  {
                    element: (
                      <ConsoleLayout>
                        <Outlet />
                      </ConsoleLayout>
                    ),
                    children: [
                      { path: "/app/profile", element: <ProfilePage /> },
                      { path: "/app/console", element: <ConsolePage /> },
                    ],
                  },
                  {
                    path: "/app/project-overview",
                    element: (
                      <ProjectOverviewLayout
                        redirectPaths={redirectPaths}
                        navigationMenus={navigationMenus}>
                        <Outlet />
                      </ProjectOverviewLayout>
                    ),
                    children: [
                      {
                        path: "/app/project-overview/environments",
                        element: <EnvironmentsPage />,
                      },
                    ],
                  },
                ],
              },
              {
                // impersonate
                element: (
                  <DashboardLayout
                    redirectPaths={redirectPaths}
                    navigationMenus={navigationMenus}>
                    <Outlet />
                  </DashboardLayout>
                ),
                children: [
                  { path: "/app/dashboard", element: <DashboardOverview /> },
                  { path: "/app/workflow/:id", element: <WorkflowDetailsPage /> },
                  { path: "/app/workflow", element: <WorkflowsPage /> },
                ],
              },
            ],
          },
        ],
      },
      {
        path: "*",
        element: <Navigate to="/app/console" replace />,
      },
    ],
  },
]);
