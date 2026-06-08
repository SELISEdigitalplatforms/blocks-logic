import { createBrowserRouter, Navigate, Outlet } from "react-router-dom";

import { AuthLayout } from "./layouts/auth-layout";
import { PublicLayout } from "./layouts/public-layout";
import { OidcLayout } from "./layouts/oidc-layout";
import { DashboardLayout } from "./layouts/dashboard-layout";

// Auth routes (public, with auth layout)
import Homepage from "./routes/public/auth/login-simple";
import SignupPage from "./routes/public/auth/signup";
import SsoActivatePage from "./routes/public/auth/sso-activate";

// Public routes (with public guard only)
import ActivatePage from "./routes/public/auth/activate";
import ForgotPasswordPage from "./routes/public/auth/forgot-password";
import ResetPasswordPage from "./routes/public/auth/resetpassword";
import ActivateSuccessPage from "./routes/public/auth/activate-success";
import ForgotEmailSentPage from "./routes/public/auth/forgot-email-sent";
import SignupEmailSentPage from "./routes/public/auth/signup-email-sent";
import MfaCheckPage from "./routes/public/auth/mfa-check";
import ResetPasswordSuccessPage from "./routes/public/auth/reset-password-success";

// OIDC routes (un-guarded)
import OidcIndexPage from "./routes/public/oidc/index";
import OidcLoginPage from "./routes/public/oidc/login";
import OidcPermissionPage from "./routes/public/oidc/permission";
import OidcErrorPage from "./routes/public/oidc/error";
import OidcForgotPasswordPage from "./routes/public/oidc/forgot-password";
import OidcEmailSentConfirmationPage from "./routes/public/oidc/email-sent-confirmation";

// Dashboard routes (protected)
// import IamPage from "./routes/public/dashboard/iam";
// import IamUserDetailPage from "./routes/public/dashboard/iam-user-detail";
// import IamRoleDetailPage from "./routes/public/dashboard/iam-role-detail";
// import IamPermissionDetailPage from "./routes/public/dashboard/iam-permission-detail";
// import IamAddPermissionPage from "./routes/public/dashboard/iam-add-permission";
// import IamOrgDetailPage from "./routes/public/dashboard/iam-org-detail";
// import IamLogsPage from "./routes/public/dashboard/iam-logs";
// import IamConfigurePage from "./routes/public/dashboard/iam-configure";
// import AuthenticationConfigPage from "./routes/public/dashboard/authentication-config";
// import SsoConfigurationPage from "./routes/public/dashboard/sso-configuration";
// import AuthLogsPage from "./routes/public/dashboard/auth-logs";
// import MfaLogsPage from "./routes/public/dashboard/mfa-logs";
// import CaptchaLogsPage from "./routes/public/dashboard/captcha-logs";
// import ApiSettingsPage from "./routes/public/dashboard/api-settings";
// import RateLimiterPage from "./routes/public/dashboard/rate-limiter";
import ProfilePage from "./routes/public/dashboard/profile";

// Console pages
import { Console } from "./pages/console/console";
import { DashboardOverview } from "./pages/dashboard/dashboard-overview";
// import { EnvironmentsPage } from "./pages/environments/environments";

import WorkflowsPage from "./routes/private/workflows/workflows-page";
import WorkflowDetailsPage from "./routes/private/workflow-details/workflow-details-page";
import LoginCallbackPage from "./routes/public/auth/callback";
import SsoConfigurationPage from "./routes/public/auth/sso-configuration";
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
          // publuc
          {
            path: "/dashboard/callback",
            element: <CallbackPage redirectUrl="/dashboard" />,
          },
          {
            element: (
              <PublicGuard>
                <Outlet />
              </PublicGuard>
            ),
            children: [
              { path: "/login", element: <LoginPage name="blocks-logic" /> },
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
