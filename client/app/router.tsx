import { createBrowserRouter, Navigate } from "react-router-dom";

import { AuthLayout } from "./layouts/auth-layout";
import { PublicLayout } from "./layouts/public-layout";
import { OidcLayout } from "./layouts/oidc-layout";
import { DashboardLayout } from "./layouts/dashboard-layout";
import { ConsoleLayout } from "./layouts/console-layout";
import { ProjectOverviewLayout } from "./layouts/project-overview-layout";

// Auth routes (public, with auth layout)
import LoginPage from "./routes/public/auth/login";
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
import IamPage from "./routes/public/dashboard/iam";
import IamUserDetailPage from "./routes/public/dashboard/iam-user-detail";
import IamRoleDetailPage from "./routes/public/dashboard/iam-role-detail";
import IamPermissionDetailPage from "./routes/public/dashboard/iam-permission-detail";
import IamAddPermissionPage from "./routes/public/dashboard/iam-add-permission";
import IamOrgDetailPage from "./routes/public/dashboard/iam-org-detail";
import IamLogsPage from "./routes/public/dashboard/iam-logs";
import IamConfigurePage from "./routes/public/dashboard/iam-configure";
import AuthenticationConfigPage from "./routes/public/dashboard/authentication-config";
import SsoConfigurationPage from "./routes/public/dashboard/sso-configuration";
import AuthLogsPage from "./routes/public/dashboard/auth-logs";
import MfaLogsPage from "./routes/public/dashboard/mfa-logs";
import CaptchaLogsPage from "./routes/public/dashboard/captcha-logs";
import ApiSettingsPage from "./routes/public/dashboard/api-settings";
import RateLimiterPage from "./routes/public/dashboard/rate-limiter";
import ProfilePage from "./routes/public/dashboard/profile";

// Console pages
import { Console } from "./pages/console/console";
import WorkflowsPage from "./routes/private/workflows/workflows-page";
import WorkflowDetailsPage from "./routes/private/workflow-details/workflow-details-page";

export const router = createBrowserRouter([
  // ── Auth layout (login, signup, sso-activate) ──
  {
    element: <AuthLayout />,
    children: [
      { path: "/login-classic", element: <LoginPage /> },
      { path: "/signup", element: <SignupPage /> },
      { path: "/sso-activate", element: <SsoActivatePage /> },
    ],
  },

  // ── Simple login (no guards, no API calls) ──
  { path: "/login", element: <Homepage /> },

  // ── Public layout (other public pages with PublicGuard) ──
  {
    element: <PublicLayout />,
    children: [
      { path: "/activate", element: <ActivatePage /> },
      { path: "/forgot-password", element: <ForgotPasswordPage /> },
      { path: "/resetpassword", element: <ResetPasswordPage /> },
      { path: "/activate-success", element: <ActivateSuccessPage /> },
      { path: "/forgot-email-sent", element: <ForgotEmailSentPage /> },
      { path: "/signup-email-sent", element: <SignupEmailSentPage /> },
      { path: "/mfa-check", element: <MfaCheckPage /> },
      {
        path: "/reset-password-success",
        element: <ResetPasswordSuccessPage />,
      },
    ],
  },

  // ── OIDC layout (un-guarded, themed) ──
  {
    path: "/oidc",
    element: <OidcLayout />,
    children: [
      { index: true, element: <OidcIndexPage /> },
      { path: "login", element: <OidcLoginPage /> },
      { path: "permission", element: <OidcPermissionPage /> },
      { path: "error", element: <OidcErrorPage /> },
      { path: "forgot-password", element: <OidcForgotPasswordPage /> },
      {
        path: "email-sent-confirmation",
        element: <OidcEmailSentConfirmationPage />,
      },
    ],
  },

  // ── Dashboard layout (protected routes) ──
  {
    element: <DashboardLayout />,
    children: [
      { path: "/services/iam", element: <IamPage /> },
      { path: "/services/iam/user-detail/:id", element: <IamUserDetailPage /> },
      { path: "/services/iam/role-detail/:id", element: <IamRoleDetailPage /> },
      {
        path: "/services/iam/permission-detail/new",
        element: <IamAddPermissionPage />,
      },
      {
        path: "/services/iam/permission-detail/:id",
        element: <IamPermissionDetailPage />,
      },
      {
        path: "/services/iam/organization-detail/:itemId",
        element: <IamOrgDetailPage />,
      },
      { path: "/services/iam/logs", element: <IamLogsPage /> },
      { path: "/services/iam/configure", element: <IamConfigurePage /> },
      {
        path: "/services/authentication",
        element: <AuthenticationConfigPage />,
      },
      {
        path: "/services/authentication/sso-configuration",
        element: <SsoConfigurationPage />,
      },
      { path: "/services/authentication/logs", element: <AuthLogsPage /> },
      {
        path: "/services/mfa",
        element: <Navigate to="/services/secret-management?tab=mfa" replace />,
      },
      { path: "/services/mfa/logs", element: <MfaLogsPage /> },
      { path: "/services/api-settings", element: <ApiSettingsPage /> },
      { path: "/services/rate-limiter", element: <RateLimiterPage /> },
      {
        path: "/services/captcha",
        element: (
          <Navigate to="/services/secret-management?tab=captcha" replace />
        ),
      },
      { path: "/services/captcha/logs", element: <CaptchaLogsPage /> },
      { path: "/workflow/:id", element: <WorkflowDetailsPage /> },
      { path: "/workflow", element: <WorkflowsPage /> },
    ],
  },

  // ── Console layout (profile, console pages without sidebar) ──
  {
    element: <ConsoleLayout />,
    children: [
      { path: "/profile", element: <ProfilePage /> },
      { path: "/console", element: <Console /> },
      // { path: "/create-project", element: <CreateProjectWrapper /> },
      // { path: "/callback", element: <CallbackPage /> },
    ],
  },

  // ── Dashboard and project overview in dashboard layout (consolidated sidebar) ──
  {
    element: <DashboardLayout />,
    children: [
      // { path: "/dashboard", element: <DashboardOverview /> },
      {
        path: "/project-overview",
        element: <Navigate to="/project-overview/environments" replace />,
      },
    //   { path: "/project-overview/environments", element: <EnvironmentsPage /> },
    //   { path: "/project-overview/people", element: <PeopleManagement /> },
    //   { path: "/project-overview/repositories", element: <RepositoriesPage /> },
    //   { path: "/project-overview/settings", element: <SettingsPage /> },
    ],
  },

  // ── Root redirect: authenticated users go to console ──
  { path: "/", element: <Navigate to="/console" replace /> },

  // ── Catch-all: redirect to login ──
  { path: "*", element: <Navigate to="/login" replace /> },
]);
