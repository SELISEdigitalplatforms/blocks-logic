import { createBrowserRouter, Navigate, Outlet } from "react-router-dom";
import { DashboardLayout } from "./layouts/dashboard-layout";
import { DashboardOverview } from "./pages/dashboard/dashboard-overview";

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
} from "@seliseblocks/blocks-kit";
import { EnvironmentsPage } from "./pages/environments/environments";
import { ProjectOverviewLayout } from "./layouts/project-overview-layout";

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
            element: <CallbackPage redirectUrl="/console" />,
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
                  <ImpersonationChecker>
                    <ImpersonationTerminator>
                      <Outlet />
                    </ImpersonationTerminator>
                  </ImpersonationChecker>
                ),
                children: [
                  {
                    element: (
                      <ConsoleLayout>
                        <Outlet />
                      </ConsoleLayout>
                    ),
                    children: [
                      { path: "/profile", element: <ProfilePage /> },
                      { path: "/console", element: <ConsolePage /> },
                    ],
                  },
                  {
                    element: <ProjectOverviewLayout />,
                    children: [
                      {
                        path: "/project-overview/environments",
                        element: <EnvironmentsPage />,
                      },
                    ],
                  },
                ],
              },
              {
                // impersonate
                element: (
                  <ImpersonationChecker>
                    <ImpersonationSynchronizer>
                      <DashboardLayout />
                    </ImpersonationSynchronizer>
                  </ImpersonationChecker>
                ),
                children: [
                  { path: "/dashboard", element: <DashboardOverview /> },
                  { path: "/workflow/:id", element: <WorkflowDetailsPage /> },
                  { path: "/workflow", element: <WorkflowsPage /> },
                ],
              },
            ],
          },
        ],
      },
      {
        path: "*",
        element: <Navigate to="/console" replace />,
      },
    ],
  },
]);
