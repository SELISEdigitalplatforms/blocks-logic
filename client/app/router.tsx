import { createBrowserRouter, Navigate } from "react-router-dom";

import { AuthLayout } from "./layouts/auth-layout";
import { PublicLayout } from "./layouts/public-layout";
import { OidcLayout } from "./layouts/oidc-layout";
import { AdminLayout } from "./layouts/admin-layout";
import { ConsoleLayout } from "./layouts/console-layout";
import { ProjectOverviewLayout } from "./layouts/project-overview-layout";

// Auth routes (public, with auth layout)
import LoginPage from "./routes/auth/login";
import SignupPage from "./routes/auth/signup";
import SsoActivatePage from "./routes/auth/sso-activate";

// Public routes (with public guard only)
import ActivatePage from "./routes/auth/activate";
import ForgotPasswordPage from "./routes/auth/forgot-password";
import ResetPasswordPage from "./routes/auth/resetpassword";
import ActivateSuccessPage from "./routes/auth/activate-success";
import ForgotEmailSentPage from "./routes/auth/forgot-email-sent";
import SignupEmailSentPage from "./routes/auth/signup-email-sent";
import MfaCheckPage from "./routes/auth/mfa-check";
import ResetPasswordSuccessPage from "./routes/auth/reset-password-success";

// OIDC routes (un-guarded)
import OidcIndexPage from "./routes/oidc/index";
import OidcLoginPage from "./routes/oidc/login";
import OidcPermissionPage from "./routes/oidc/permission";
import OidcErrorPage from "./routes/oidc/error";
import OidcForgotPasswordPage from "./routes/oidc/forgot-password";
import OidcEmailSentConfirmationPage from "./routes/oidc/email-sent-confirmation";

// Admin routes (protected)
import IamPage from "./routes/admin/iam";
import IamUserDetailPage from "./routes/admin/iam-user-detail";
import IamRoleDetailPage from "./routes/admin/iam-role-detail";
import IamPermissionDetailPage from "./routes/admin/iam-permission-detail";
import IamAddPermissionPage from "./routes/admin/iam-add-permission";
import IamOrgDetailPage from "./routes/admin/iam-org-detail";
import IamLogsPage from "./routes/admin/iam-logs";
import IamConfigurePage from "./routes/admin/iam-configure";
import AuthenticationConfigPage from "./routes/admin/authentication-config";
import SsoConfigurationPage from "./routes/admin/sso-configuration";
import AuthLogsPage from "./routes/admin/auth-logs";
import MfaConfigPage from "./routes/admin/mfa-config";
import MfaLogsPage from "./routes/admin/mfa-logs";
import CaptchaConfigPage from "./routes/admin/captcha-config";
import CaptchaLogsPage from "./routes/admin/captcha-logs";
import ProfilePage from "./routes/admin/profile";

// Console pages
import { Console } from "./pages/console/console";
import { DashboardOverview } from "./pages/dashboard/dashboard-overview";
import { EnvironmentsPage } from "./pages/environments/environments";
import { PeopleManagement } from "./pages/people/people-management";
import { RepositoriesPage } from "./pages/repositories/repositories";
import { SettingsPage } from "./pages/settings/settings";
import { CreateProjectWrapper } from "./pages/create-project/create-project";
import CallbackPage from "./routes/callback/callback";

export const router = createBrowserRouter([
  // ── Auth layout (login, signup, sso-activate) ──
  {
    element: <AuthLayout />,
    children: [
      { path: "/login", element: <LoginPage /> },
      { path: "/signup", element: <SignupPage /> },
      { path: "/sso-activate", element: <SsoActivatePage /> },
    ],
  },

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
      { path: "/reset-password-success", element: <ResetPasswordSuccessPage /> },
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
      { path: "email-sent-confirmation", element: <OidcEmailSentConfirmationPage /> },
    ],
  },

  // ── Admin layout (protected routes) ──
  {
    element: <AdminLayout />,
    children: [
      { path: "/services/iam", element: <IamPage /> },
      { path: "/services/iam/user-detail/:id", element: <IamUserDetailPage /> },
      { path: "/services/iam/role-detail/:id", element: <IamRoleDetailPage /> },
      { path: "/services/iam/permission-detail/new", element: <IamAddPermissionPage /> },
      { path: "/services/iam/permission-detail/:id", element: <IamPermissionDetailPage /> },
      { path: "/services/iam/organization-detail/:itemId", element: <IamOrgDetailPage /> },
      { path: "/services/iam/logs", element: <IamLogsPage /> },
      { path: "/services/iam/configure", element: <IamConfigurePage /> },
      { path: "/services/authentication", element: <AuthenticationConfigPage /> },
      { path: "/services/authentication/sso-configuration", element: <SsoConfigurationPage /> },
      { path: "/services/authentication/logs", element: <AuthLogsPage /> },
      { path: "/services/mfa", element: <MfaConfigPage /> },
      { path: "/services/mfa/logs", element: <MfaLogsPage /> },
      { path: "/services/captcha", element: <CaptchaConfigPage /> },
      { path: "/services/captcha/logs", element: <CaptchaLogsPage /> },
    ],
  },

  // ── Console layout (profile, console pages without sidebar) ──
  {
    element: <ConsoleLayout />,
    children: [
      { path: "/profile", element: <ProfilePage /> },
      { path: "/console", element: <Console /> },
      { path: "/create-project", element: <CreateProjectWrapper /> },
      { path: "/callback", element: <CallbackPage /> },
    ],
  },

  // ── Dashboard and project overview in admin layout (consolidated sidebar) ──
  {
    element: <AdminLayout />,
    children: [
      { path: "/dashboard", element: <DashboardOverview /> },
      { path: "/project-overview", element: <Navigate to="/project-overview/environments" replace /> },
      { path: "/project-overview/environments", element: <EnvironmentsPage /> },
      { path: "/project-overview/people", element: <PeopleManagement /> },
      { path: "/project-overview/repositories", element: <RepositoriesPage /> },
      { path: "/project-overview/settings", element: <SettingsPage /> },
    ],
  },

  // ── Root redirect: authenticated users go to console ──
  { path: "/", element: <Navigate to="/console" replace /> },

  // ── Catch-all: redirect to login ──
  { path: "*", element: <Navigate to="/login" replace /> },
]);
