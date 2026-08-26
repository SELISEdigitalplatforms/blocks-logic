import { createBrowserRouter, Navigate, Outlet } from "react-router";

import WorkflowsPage from "./routes/private/workflows/workflows-page";
import SchedulesPage from "./routes/private/schedules/schedules-page";
import ScheduleCreatePage from "./routes/private/schedules/schedule-create-page";
import ScheduleEditPage from "./routes/private/schedules/schedule-edit-page";
import ScheduleDetailsPage from "./routes/private/schedules/schedule-details-page";
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
  DashboardOverview,
  DashboardRoute,
} from "@seliseblocks/genesis-os";
import { navigationMenus } from "./constants/navigation-menus";

const redirectPaths: Record<string, string> = {
  "/workflow/*": "/app/workflow",
  "/schedules/*": "/app/schedule",
  "/schedule/*": "/app/schedule",
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
