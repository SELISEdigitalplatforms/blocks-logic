import { useQueryState } from "nuqs";
import { SSO } from "@blocks-idp/authentication/pages/authentication-config/sso";
import { GRANT_TYPES } from "@blocks-idp/authentication/constants/authentication.constant";
import { AIModels } from "@blocks-ai/pages/aimodels";
import { OIDC } from "@blocks-idp/authentication/components/oidc";
import { Certificates } from "@blocks-idp/authentication/pages/authentication-config/general/certificates/certificates";
import { CreateOIDC } from "@blocks-idp/authentication/components/create-oidc";
import { ConfigureCaptcha } from "@blocks-idp/captcha/pages/configure-captcha";
import { ConfigureCaptchaModal } from "@blocks-idp/captcha/modals/configure-captcha-modal";
import { ConfigureMFA } from "@blocks-idp/mfa/pages/configure-mfa/configure-mfa";
import { MagicUrlConfigDialog } from "@blocks-utilities/components/magic-url-config-dialog/magic-url-config-dialog";
import { useSaveMagicUrlConfig } from "@blocks-utilities/hooks/use-magic-url";
import { StorageContents } from "@blocks-storage/pages/storage/storage-contents";
import { ManagedServices } from "@blocks-identifier/pages/services/managed-services";
import { AddService } from "@blocks-identifier/components/add-service/add-service";
import { EmailConfiguration } from "@blocks-communication/mail/email/email-configure/email-configure";
import NotificationConfigurationList from "@blocks-communication/notification/components/notification-configuration-list";
import { Button } from "@/components/ui-kits/button/button";
import { Sheet, SheetContent, SheetTrigger, SheetClose } from "@/components/ui-kits/sheet/sheet";
import {
  CirclePlus,
  Settings,
  Notebook,
  AlertCircle,
  KeyRound,
  Lock,
  Layers,
  ShieldCheck,
  Users,
  Globe,
  ShieldAlert,
  Smartphone,
  Link2,
  Mail,
  Bell,
  Database,
  BrainCircuit,
  Server,
  Menu,
  ChevronsLeft,
  X,
  type LucideIcon,
} from "lucide-react";
import { MouseEvent, useEffect, useMemo, useState } from "react";
import { useLocation } from "react-router-dom";
import { CAPTCHA_PROVIDERS, CAPTCHA_PROVIDERS_KEY } from "@blocks-idp/captcha/models/captcha";
import { useGetCaptchaConfigs } from "@blocks-idp/captcha/hooks/use-captcha-config";
import { useProjectStore } from "@/store/useProjectStore";
import { DialogTrigger } from "@radix-ui/react-dialog";
import { toast } from "@/hooks/use-toast";

type NavItem = {
  id: string;
  label: string;
  value: string;
  icon: LucideIcon;
  desc: string;
};

type NavGroup = {
  label: string;
  icon?: LucideIcon;
  items: NavItem[];
};

const navGroups: NavGroup[] = [
  {
    label: "Secrets & Keys",
    items: [
      { id: "infra-config", label: "Infra Config", value: "infra-config", icon: Server, desc: "Manage infrastructure configurations" },
      { id: "my-secret", label: "My Secret", value: "my-secret", icon: KeyRound, desc: "Manage your secrets and credentials" },
      { id: "managed-services", label: "My Service", value: "managed-services", icon: Layers, desc: "Manage connected services" },
    ],
  },
  {
    label: "Authentication",
    items: [
      { id: GRANT_TYPES.authorizationCode, label: "OIDC", value: GRANT_TYPES.authorizationCode, icon: ShieldCheck, desc: "OpenID Connect configuration" },
      { id: GRANT_TYPES.social, label: "SSO", value: GRANT_TYPES.social, icon: Users, desc: "Single sign-on providers" },
      { id: "external-idp", label: "External IdP", value: "external-idp", icon: Globe, desc: "External identity providers & certificates" },
      { id: "captcha", label: "Captcha", value: "captcha", icon: ShieldAlert, desc: "Bot protection configuration" },
      { id: "mfa", label: "MFA", value: "mfa", icon: Smartphone, desc: "Multi-factor authentication settings" },
      { id: "magic-url", label: "Magic URL", value: "magic-url", icon: Link2, desc: "Passwordless magic link settings" },
    ],
  },
  {
    label: "Communication",
    items: [
      { id: "email", label: "Email", value: "email", icon: Mail, desc: "Email provider configuration" },
      { id: "notification", label: "Notification", value: "notification", icon: Bell, desc: "Push & notification settings" },
    ],
  },
  {
    label: "Intelligence",
    items: [
      { id: "storage", label: "Storage", value: "storage", icon: Database, desc: "File and object storage" },
      { id: "ai-models", label: "AI Models", value: "ai-models", icon: BrainCircuit, desc: "AI model integrations" },
    ],
  },
];

const allNavItems = navGroups.flatMap((g) => g.items);
const HIDDEN_BANNER_TABS = ["my-secret", "managed-services", "ai-models"];

export default function SecretManagementPage() {
  const [selectedTab, setSelectedTab] = useQueryState("tab", { defaultValue: "infra-config" });
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { data: captchaData } = useGetCaptchaConfigs({ projectKey: tenantId });
  const { mutateAsync: saveMagicUrlConfig } = useSaveMagicUrlConfig();
  const [isMagicUrlConfigDialogOpen, setIsMagicUrlConfigDialogOpen] = useState(false);
  const [isManagedServicesGuideOpen, setIsManagedServicesGuideOpen] = useState(false);
  const [isEmailConfigOpen, setIsEmailConfigOpen] = useState(false);
  const [isNotificationConfigOpen, setIsNotificationConfigOpen] = useState(false);
  const [isMobileSidebarOpen, setIsMobileSidebarOpen] = useState(false);
  const [isDesktopSidebarOpen, setIsDesktopSidebarOpen] = useState(true);
  const location = useLocation();

  useEffect(() => {
    if (!isDesktopSidebarOpen) {
      setIsDesktopSidebarOpen(true);
    }
  }, [location.key]);

  const areAllProvidersConfigured = useMemo(() => {
    if (!captchaData?.configurations) return false;
    const allProviderKeys = Object.keys(CAPTCHA_PROVIDERS) as CAPTCHA_PROVIDERS_KEY[];
    const configuredProviders = new Set(
      captchaData.configurations.map((config: { provider: string }) => config.provider),
    );
    return allProviderKeys.every((key) => configuredProviders.has(key));
  }, [captchaData]);

  const addConfigurationHandler = (e: MouseEvent) => {
    if (areAllProvidersConfigured) {
      toast({
        variant: "info",
        title: "Info",
        description: "No additional captcha configurations can be added.",
      });
      return e.preventDefault();
    }
  };

  const currentItem = allNavItems.find((item) => item.value === selectedTab) ?? allNavItems[0];

  const handleTabChange = (value: string) => {
    setSelectedTab(value);
    setIsMobileSidebarOpen(false);
  };

  const SidebarNav = ({ showCollapse = false }: { showCollapse?: boolean }) => (
    <nav className="flex min-h-0 flex-1 flex-col overflow-hidden">
      <div className="flex-1 overflow-y-auto py-1">
        {navGroups.map((group, idx) => (
          <div key={group.label}>
            <div className="flex items-center justify-between px-4 pb-1 pt-3">
              <div className="flex items-center gap-1.5">
                {group.icon && <group.icon className="h-3.5 w-3.5 text-muted-foreground/50" />}
                <p className="text-[10px] font-semibold uppercase tracking-[0.15em] text-muted-foreground/50">
                  {group.label}
                </p>
              </div>
              {showCollapse && idx === 0 && (
                <Button
                  variant="ghost"
                  size="icon"
                  className="h-6 w-6 text-muted-foreground hover:text-foreground"
                  onClick={() => setIsDesktopSidebarOpen(false)}
                >
                  <ChevronsLeft className="h-4 w-4" />
                </Button>
              )}
            </div>
            {group.items.map((item) => {
              const Icon = item.icon;
              const isActive = selectedTab === item.value;
              return (
                <button
                  key={item.id}
                  onClick={() => handleTabChange(item.value)}
                  className={`relative flex h-10 w-full cursor-pointer items-center gap-3 px-4 py-1.5 text-sm transition-colors ${
                    isActive
                      ? "text-primary"
                      : "text-[hsl(var(--low-emphasis))] hover:text-[hsl(var(--high-emphasis))]"
                  }`}
                >
                  <Icon className="h-5 w-5 shrink-0" />
                  <span>{item.label}</span>
                  {isActive && (
                    <div className="absolute right-0 top-2.5 h-5 w-1 rounded-l-lg bg-primary" />
                  )}
                </button>
              );
            })}
          </div>
        ))}
      </div>
    </nav>
  );

  return (
    <div className="flex h-full min-h-0 overflow-hidden">
      {/* Desktop Sidebar */}
      {isDesktopSidebarOpen && (
        <aside className="hidden w-52 shrink-0 border-r border-border bg-card lg:flex lg:flex-col lg:h-screen">
          <SidebarNav showCollapse={true} />
        </aside>
      )}

      {/* Main content */}
      <div className="flex min-w-0 flex-1 flex-col">
        {/* Content header */}
        <div className="flex items-center justify-between px-6 py-4">
          <div className="flex items-center gap-3">
            {/* Mobile sidebar trigger */}
            <Sheet open={isMobileSidebarOpen} onOpenChange={setIsMobileSidebarOpen}>
              <SheetTrigger asChild>
                <Button variant="outline" size="icon" className="h-8 w-8 lg:hidden">
                  <Menu className="h-4 w-4" />
                </Button>
              </SheetTrigger>
              <SheetContent side="left" className="w-52 p-0">
                <div className="flex h-full flex-col">
                  <div className="flex justify-end border-b border-border p-3">
                    <SheetClose asChild>
                      <Button variant="ghost" size="icon" className="h-8 w-8">
                        <X className="h-4 w-4" />
                        <span className="sr-only">Close sidebar</span>
                      </Button>
                    </SheetClose>
                  </div>
                  <SidebarNav showCollapse={false} />
                </div>
              </SheetContent>
            </Sheet>
            <div>
              <div className="flex items-center gap-2">
                {(() => {
                  const Icon = currentItem.icon;
                  return <Icon className="h-4 w-4 text-primary" />;
                })()}
                <h1 className="text-base font-semibold text-[hsl(var(--high-emphasis))]">{currentItem.label}</h1>
              </div>
              <p className="text-xs text-muted-foreground">{currentItem.desc}</p>
            </div>
          </div>
          <div className="flex items-center gap-2">
            {selectedTab === GRANT_TYPES.authorizationCode && <CreateOIDC />}
            {selectedTab === "captcha" && (
              <ConfigureCaptchaModal>
                <DialogTrigger asChild>
                  <Button size="sm" onClick={addConfigurationHandler}>
                    <CirclePlus className="h-5 w-5" />
                    <span className="sr-only sm:not-sr-only sm:ml-2.5 sm:text-sm sm:whitespace-nowrap">Add Configuration</span>
                  </Button>
                </DialogTrigger>
              </ConfigureCaptchaModal>
            )}
            {selectedTab === "magic-url" && (
              <>
                <Button variant="outline" size="sm" onClick={() => setIsMagicUrlConfigDialogOpen(true)}>
                  <Settings className="h-5 w-5" />
                  <span className="sr-only sm:not-sr-only sm:ml-2.5 sm:text-sm sm:whitespace-nowrap">Configure</span>
                </Button>
                <MagicUrlConfigDialog
                  open={isMagicUrlConfigDialogOpen}
                  onOpenChange={setIsMagicUrlConfigDialogOpen}
                  projectKey={tenantId}
                  onSave={async (config) => {
                    await saveMagicUrlConfig(config);
                  }}
                />
              </>
            )}
            {selectedTab === "managed-services" && (
              <>
                <Button variant="outline" size="sm" onClick={() => setIsManagedServicesGuideOpen(true)}>
                  <Notebook className="aspect-square w-4" />
                  <span className="sr-only sm:not-sr-only sm:ml-2 sm:text-sm sm:whitespace-nowrap">Setup Guide</span>
                </Button>
                <AddService />
              </>
            )}
            {selectedTab === "email" && (
              <Button size="sm" onClick={() => setIsEmailConfigOpen(true)}>
                <CirclePlus className="h-5 w-5" />
                <span className="sr-only sm:not-sr-only sm:ml-2.5 sm:text-sm sm:whitespace-nowrap">Add Configuration</span>
              </Button>
            )}
            {selectedTab === "notification" && (
              <Button size="sm" onClick={() => setIsNotificationConfigOpen(true)}>
                <CirclePlus className="h-5 w-5" />
                <span className="sr-only sm:not-sr-only sm:ml-2.5 sm:text-sm sm:whitespace-nowrap">Add Configuration</span>
              </Button>
            )}
          </div>
        </div>

        {/* Content body */}
        <div className="flex-1 overflow-y-auto p-6">
          {!HIDDEN_BANNER_TABS.includes(selectedTab) && (
            <div className="mb-4 flex items-start gap-3 rounded-lg border border-amber-200 bg-amber-50 p-4 dark:border-amber-900/30 dark:bg-amber-950/20">
              <AlertCircle className="mt-0.5 h-5 w-5 flex-shrink-0 text-amber-600 dark:text-amber-500" />
              <div className="flex-1">
                <h4 className="font-semibold text-amber-900 dark:text-amber-100">Secret values are hidden for security</h4>
                <p className="mt-1 text-sm text-amber-800 dark:text-amber-200">
                  Once you enter secret values, they won't be displayed again for security reasons. You can only view and manage configurations.
                </p>
              </div>
            </div>
          )}

          {selectedTab === "infra-config" && (
            <div className="rounded-lg border border-border bg-card p-6">
              <h3 className="text-lg font-semibold">Infra Config</h3>
              <p className="mt-2 text-muted-foreground">Manage your infrastructure configurations</p>
            </div>
          )}
          {selectedTab === GRANT_TYPES.authorizationCode && <OIDC />}
          {selectedTab === "managed-services" && (
            <ManagedServices
              guideOpen={isManagedServicesGuideOpen}
              onGuideOpenChange={setIsManagedServicesGuideOpen}
            />
          )}
          {selectedTab === "my-secret" && (
            <div className="rounded-lg border border-border bg-card p-6">
              <h3 className="text-lg font-semibold">My Secret</h3>
              <p className="mt-2 text-muted-foreground">Manage your secrets and credentials</p>
            </div>
          )}
          {selectedTab === GRANT_TYPES.social && <SSO />}
          {selectedTab === "external-idp" && <Certificates />}
          {selectedTab === "captcha" && <ConfigureCaptcha />}
          {selectedTab === "mfa" && <ConfigureMFA />}
          {selectedTab === "magic-url" && (
            <div className="rounded-lg border border-dashed bg-background p-8 text-center text-muted-foreground">
              <p>Use the Configure button above to manage Magic URL settings.</p>
            </div>
          )}
          {selectedTab === "storage" && <StorageContents />}
          {selectedTab === "email" && (
            <EmailConfiguration
              addConfigOpen={isEmailConfigOpen}
              onAddConfigOpenChange={setIsEmailConfigOpen}
            />
          )}
          {selectedTab === "notification" && (
            <NotificationConfigurationList
              addConfigOpen={isNotificationConfigOpen}
              onAddConfigOpenChange={setIsNotificationConfigOpen}
            />
          )}
          {selectedTab === "ai-models" && <AIModels />}
        </div>
      </div>
    </div>
  );
}

