import { useCallback, useMemo } from "react";
import { useProjectStore } from "@/store/useProjectStore";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { ServiceGroupCard } from "@blocks-idp/api-settings/components/service-group-card";
import {
  useGetApiEndpoints,
  useUpdateApiEndpoint,
} from "@blocks-idp/api-settings/hooks/use-api-settings";
import { IApiEndpoint } from "@blocks-idp/api-settings/models/api-endpoint.model";

/** ─── Loading skeleton ──────────────────────────────────────────────────────── */
const ServiceGroupSkeleton = () => (
  <div className="rounded-lg border border-border bg-card p-4">
    <div className="flex items-center gap-3">
      <Skeleton className="h-5 w-5 rounded" />
      <Skeleton className="h-6 w-6 rounded" />
      <div className="flex-1 space-y-2">
        <Skeleton className="h-5 w-40" />
        <Skeleton className="h-4 w-64" />
      </div>
      <Skeleton className="h-6 w-24 rounded" />
      <Skeleton className="h-8 w-28 rounded" />
    </div>
  </div>
);

/** ─── Page component ────────────────────────────────────────────────────────── */
export default function ApiSettingsPage() {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { data, isLoading } = useGetApiEndpoints({ projectKey: tenantId, page: 0, pageSize: 100 });
  const { mutateAsync: updateEndpoint } = useUpdateApiEndpoint();

  const endpoints = data?.data ?? [];

  // Group endpoints by service
  const serviceGroups = useMemo(() => {
    const groups: Record<string, IApiEndpoint[]> = {};
    for (const ep of endpoints) {
      (groups[ep.service] ??= []).push(ep);
    }
    return Object.entries(groups).sort(([a], [b]) => a.localeCompare(b));
  }, [endpoints]);

  // ── Toggle handlers ───────────────────────────────────────────────────────
  const handleToggleMfa = useCallback(
    async (ep: IApiEndpoint, value: boolean) => {
      try {
        await updateEndpoint({
          projectKey: tenantId,
          itemId: ep.itemId,
          service: ep.service,
          method: ep.method,
          endpoint: ep.endpoint,
          description: ep.description,
          isMfaRequired: value,
          mfaType: ep.mfaType,
          isCaptchaRequired: ep.isCaptchaRequired,
          captchaProvider: ep.captchaProvider,
        });
        showSuccessToast({ description: `MFA ${value ? "enabled" : "disabled"} for ${ep.endpoint}` });
      } catch {
        showErrorToast({ errors: "Failed to update MFA setting" });
      }
    },
    [tenantId, updateEndpoint],
  );

  const handleToggleCaptcha = useCallback(
    async (ep: IApiEndpoint, value: boolean) => {
      try {
        await updateEndpoint({
          projectKey: tenantId,
          itemId: ep.itemId,
          service: ep.service,
          method: ep.method,
          endpoint: ep.endpoint,
          description: ep.description,
          isCaptchaRequired: value,
          captchaProvider: ep.captchaProvider,
          isMfaRequired: ep.isMfaRequired,
          mfaType: ep.mfaType,
        });
        showSuccessToast({ description: `Captcha ${value ? "enabled" : "disabled"} for ${ep.endpoint}` });
      } catch {
        showErrorToast({ errors: "Failed to update Captcha setting" });
      }
    },
    [tenantId, updateEndpoint],
  );

  return (
    <main className="flex flex-col gap-6 p-6 pb-24">
      {/* Header */}
      <div>
        <h1 className="text-xl font-semibold md:text-2xl">API Settings</h1>
        <p className="text-muted-foreground">
          Configure security policies for your API endpoints — enable MFA, Captcha, and manage access controls.
        </p>
      </div>

      {/* Service groups */}
      {isLoading ? (
        <div className="flex flex-col gap-4">
          {Array.from({ length: 3 }).map((_, i) => (
            <ServiceGroupSkeleton key={i} />
          ))}
        </div>
      ) : serviceGroups.length === 0 ? (
        <div className="flex h-40 items-center justify-center rounded-lg border border-dashed bg-card text-muted-foreground">
          No API endpoints configured.
        </div>
      ) : (
        <div className="flex flex-col gap-4">
          {serviceGroups.map(([service, eps]) => (
            <ServiceGroupCard
              key={service}
              service={service}
              endpoints={eps}
              onToggleMfa={handleToggleMfa}
              onToggleCaptcha={handleToggleCaptcha}
            />
          ))}
        </div>
      )}


    </main>
  );
}
