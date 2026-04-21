import { useCallback, useMemo, useState } from "react";
import { useProjectStore } from "@/store/useProjectStore";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { ServiceGroupCard } from "@blocks-idp/api-settings/components/service-group-card";
import { BulkActionBar } from "@blocks-idp/api-settings/components/bulk-action-bar";
import {
  useGetApiEndpoints,
  useUpdateApiEndpoint,
  useBulkUpdateApiEndpoints,
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
  const { mutateAsync: bulkUpdate } = useBulkUpdateApiEndpoints();

  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());

  const endpoints = data?.data ?? [];

  // Group endpoints by service
  const serviceGroups = useMemo(() => {
    const groups: Record<string, IApiEndpoint[]> = {};
    for (const ep of endpoints) {
      (groups[ep.service] ??= []).push(ep);
    }
    return Object.entries(groups).sort(([a], [b]) => a.localeCompare(b));
  }, [endpoints]);

  // ── Selection handlers ──────────────────────────────────────────────────────
  const handleSelectEndpoint = useCallback((id: string, checked: boolean) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      checked ? next.add(id) : next.delete(id);
      return next;
    });
  }, []);

  const handleSelectGroup = useCallback((ids: string[], checked: boolean) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      ids.forEach((id) => (checked ? next.add(id) : next.delete(id)));
      return next;
    });
  }, []);

  const clearSelection = useCallback(() => setSelectedIds(new Set()), []);

  // ── Toggle handlers ────────────────────────────────────────────────────────
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

  // ── Bulk handlers (group presets) ─────────────────────────────────────────
  const handleBulkGroupMfa = useCallback(
    async (ids: string[], value: boolean) => {
      try {
        // Preserve current Captcha state when toggling MFA
        const groupEndpoints = endpoints.filter((ep) => ids.includes(ep.itemId));
        const captchaState =
          groupEndpoints.length > 0
            ? groupEndpoints.every((ep) => ep.isCaptchaRequired)
              ? true
              : groupEndpoints.some((ep) => ep.isCaptchaRequired)
                ? false // default to false if mixed states
                : false
            : false;

        await bulkUpdate({
          projectKey: tenantId,
          itemIds: ids,
          isMfaRequired: value,
          isCaptchaRequired: captchaState,
          disableAll: false,
        });
        showSuccessToast({ description: `MFA ${value ? "enabled" : "disabled"} for ${ids.length} endpoints` });
      } catch {
        showErrorToast({ errors: "Failed to bulk update MFA" });
      }
    },
    [tenantId, endpoints, bulkUpdate],
  );

  const handleBulkGroupCaptcha = useCallback(
    async (ids: string[], value: boolean) => {
      try {
        // Preserve current MFA state when toggling Captcha
        const groupEndpoints = endpoints.filter((ep) => ids.includes(ep.itemId));
        const mfaState =
          groupEndpoints.length > 0
            ? groupEndpoints.every((ep) => ep.isMfaRequired)
              ? true
              : groupEndpoints.some((ep) => ep.isMfaRequired)
                ? false // default to false if mixed states
                : false
            : false;

        await bulkUpdate({
          projectKey: tenantId,
          itemIds: ids,
          isCaptchaRequired: value,
          isMfaRequired: mfaState,
          disableAll: false,
        });
        showSuccessToast({ description: `Captcha ${value ? "enabled" : "disabled"} for ${ids.length} endpoints` });
      } catch {
        showErrorToast({ errors: "Failed to bulk update Captcha" });
      }
    },
    [tenantId, endpoints, bulkUpdate],
  );

  const handleBulkGroupDisableAll = useCallback(
    async (ids: string[]) => {
      try {
        await bulkUpdate({ projectKey: tenantId, itemIds: ids, isMfaRequired: false, isCaptchaRequired: false, disableAll: true });
        showSuccessToast({ description: `All security features disabled for ${ids.length} endpoints` });
      } catch {
        showErrorToast({ errors: "Failed to disable security features" });
      }
    },
    [tenantId, bulkUpdate],
  );

  // ── Bulk bar actions ───────────────────────────────────────────────────────
  const selectedArray = useMemo(() => Array.from(selectedIds), [selectedIds]);

  const handleBulkMfa = useCallback(async () => {
    try {
      // Preserve current Captcha state when enabling MFA
      const selectedEndpoints = endpoints.filter((ep) => selectedArray.includes(ep.itemId));
      const captchaState =
        selectedEndpoints.length > 0
          ? selectedEndpoints.every((ep) => ep.isCaptchaRequired)
            ? true
            : selectedEndpoints.some((ep) => ep.isCaptchaRequired)
              ? false // default to false if mixed states
              : false
          : false;

      await bulkUpdate({
        projectKey: tenantId,
        itemIds: selectedArray,
        isMfaRequired: true,
        isCaptchaRequired: captchaState,
        disableAll: false,
      });
      showSuccessToast({ description: `MFA enabled for ${selectedArray.length} endpoints` });
      clearSelection();
    } catch {
      showErrorToast({ errors: "Failed to enable MFA" });
    }
  }, [tenantId, endpoints, selectedArray, bulkUpdate, clearSelection]);

  const handleBulkCaptcha = useCallback(async () => {
    try {
      // Preserve current MFA state when enabling Captcha
      const selectedEndpoints = endpoints.filter((ep) => selectedArray.includes(ep.itemId));
      const mfaState =
        selectedEndpoints.length > 0
          ? selectedEndpoints.every((ep) => ep.isMfaRequired)
            ? true
            : selectedEndpoints.some((ep) => ep.isMfaRequired)
              ? false // default to false if mixed states
              : false
          : false;

      await bulkUpdate({
        projectKey: tenantId,
        itemIds: selectedArray,
        isCaptchaRequired: true,
        isMfaRequired: mfaState,
        disableAll: false,
      });
      showSuccessToast({ description: `Captcha enabled for ${selectedArray.length} endpoints` });
      clearSelection();
    } catch {
      showErrorToast({ errors: "Failed to enable Captcha" });
    }
  }, [tenantId, endpoints, selectedArray, bulkUpdate, clearSelection]);

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
              selectedIds={selectedIds}
              onSelectEndpoint={handleSelectEndpoint}
              onSelectGroup={handleSelectGroup}
              onToggleMfa={handleToggleMfa}
              onToggleCaptcha={handleToggleCaptcha}
              onBulkGroupMfa={handleBulkGroupMfa}
              onBulkGroupCaptcha={handleBulkGroupCaptcha}
            />
          ))}
        </div>
      )}

      {/* Bulk action bar */}
      <BulkActionBar
        selectedCount={selectedIds.size}
        onEnableMfa={handleBulkMfa}
        onEnableCaptcha={handleBulkCaptcha}
        onClear={clearSelection}
      />
    </main>
  );
}
