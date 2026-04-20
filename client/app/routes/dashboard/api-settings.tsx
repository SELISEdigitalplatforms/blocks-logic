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
  useRemoveApiEndpoints,
} from "@blocks-idp/api-settings/hooks/use-api-settings";
import { IApiEndpoint } from "@blocks-idp/api-settings/models/api-endpoint.model";
import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { Dialog } from "@/components/ui-kits/dialog/dialog";

/** ─── Mock seed data (used until the backend API is available) ─────────────── */
const MOCK_ENDPOINTS: IApiEndpoint[] = [
  {
    itemId: "0141ebe4-f555-44ce-90d8-db702da8e483",
    createdDate: "",
    lastUpdatedDate: "2026-04-19T14:26:24.980Z",
    createdBy: null,
    lastUpdatedBy: "",
    language: null,
    organizationIds: [],
    tags: [],
    service: "Authentication",
    method: "POST",
    endpoint: "/v1/auth/login",
    description: "Authenticate user credentials and issue JWT tokens.",
    isCaptchaRequired: true,
    captchaProvider: "",
    isMfaRequired: true,
    mfaType: "",
  },
  {
    itemId: "a2b3c4d5-e6f7-8901-abcd-ef0123456789",
    createdDate: "",
    lastUpdatedDate: "2026-04-19T14:26:24.980Z",
    createdBy: null,
    lastUpdatedBy: "",
    language: null,
    organizationIds: [],
    tags: [],
    service: "Authentication",
    method: "POST",
    endpoint: "/Authentication/Logout",
    description: "Logs out the current user session using the provided refresh token.",
    isCaptchaRequired: false,
    captchaProvider: "",
    isMfaRequired: false,
    mfaType: "",
  },
  {
    itemId: "b3c4d5e6-f789-0123-bcde-f01234567890",
    createdDate: "",
    lastUpdatedDate: "2026-04-19T14:26:24.980Z",
    createdBy: null,
    lastUpdatedBy: "",
    language: null,
    organizationIds: [],
    tags: [],
    service: "Authentication",
    method: "DELETE",
    endpoint: "/v1/users/{id}",
    description: "Permanently remove user account and associated data.",
    isCaptchaRequired: false,
    captchaProvider: "",
    isMfaRequired: true,
    mfaType: "",
  },
  {
    itemId: "c4d5e6f7-8901-2345-cdef-012345678901",
    createdDate: "",
    lastUpdatedDate: "2026-04-19T14:26:24.980Z",
    createdBy: null,
    lastUpdatedBy: "",
    language: null,
    organizationIds: [],
    tags: [],
    service: "Authentication",
    method: "POST",
    endpoint: "/Authentication/Token",
    description: "Issue or refresh access and refresh tokens.",
    isCaptchaRequired: false,
    captchaProvider: "",
    isMfaRequired: false,
    mfaType: "",
  },
  {
    itemId: "d5e6f789-0123-4567-def0-123456789012",
    createdDate: "",
    lastUpdatedDate: "2026-04-19T14:26:24.980Z",
    createdBy: null,
    lastUpdatedBy: "",
    language: null,
    organizationIds: [],
    tags: [],
    service: "Authentication",
    method: "POST",
    endpoint: "/Authentication/GetLoginOptions",
    description: "Retrieve available login methods for a project.",
    isCaptchaRequired: false,
    captchaProvider: "",
    isMfaRequired: false,
    mfaType: "",
  },
  {
    itemId: "e6f78901-2345-6789-ef01-234567890123",
    createdDate: "",
    lastUpdatedDate: "2026-04-19T14:26:24.980Z",
    createdBy: null,
    lastUpdatedBy: "",
    language: null,
    organizationIds: [],
    tags: [],
    service: "Authentication",
    method: "GET",
    endpoint: "/Authentication/GetSsoCredentials",
    description: "Retrieve configured SSO provider credentials.",
    isCaptchaRequired: false,
    captchaProvider: "",
    isMfaRequired: false,
    mfaType: "",
  },
  {
    itemId: "f7890123-4567-89ab-f012-345678901234",
    createdDate: "",
    lastUpdatedDate: "2026-04-19T14:26:24.980Z",
    createdBy: null,
    lastUpdatedBy: "",
    language: null,
    organizationIds: [],
    tags: [],
    service: "Authentication",
    method: "POST",
    endpoint: "/Authentication/SaveClientCredential",
    description: "Create or update a client credential configuration.",
    isCaptchaRequired: false,
    captchaProvider: "",
    isMfaRequired: false,
    mfaType: "",
  },
  {
    itemId: "08901234-5678-9abc-0123-456789012345",
    createdDate: "",
    lastUpdatedDate: "2026-04-19T14:26:24.980Z",
    createdBy: null,
    lastUpdatedBy: "",
    language: null,
    organizationIds: [],
    tags: [],
    service: "Authentication",
    method: "DELETE",
    endpoint: "/Authentication/DeleteClientCredential",
    description: "Remove a client credential configuration.",
    isCaptchaRequired: false,
    captchaProvider: "",
    isMfaRequired: false,
    mfaType: "",
  },
  {
    itemId: "7ce03b7b-7435-4c89-ad1a-27e0962b19fe",
    createdDate: "",
    lastUpdatedDate: "2026-04-19T14:26:24.980Z",
    createdBy: null,
    lastUpdatedBy: "",
    language: null,
    organizationIds: [],
    tags: [],
    service: "Captcha",
    method: "POST",
    endpoint: "/Captcha/Submit",
    description: "Submits a captcha answer by ID and value, returning a verification code on success.",
    isCaptchaRequired: false,
    captchaProvider: "",
    isMfaRequired: false,
    mfaType: "",
  },
  {
    itemId: "8901abcd-ef01-2345-6789-abcdef012345",
    createdDate: "",
    lastUpdatedDate: "2026-04-19T14:26:24.980Z",
    createdBy: null,
    lastUpdatedBy: "",
    language: null,
    organizationIds: [],
    tags: [],
    service: "Storage",
    method: "POST",
    endpoint: "/Storage/Upload",
    description: "Upload a file to the storage bucket.",
    isCaptchaRequired: false,
    captchaProvider: "",
    isMfaRequired: false,
    mfaType: "",
  },
  {
    itemId: "9012bcde-f012-3456-789a-bcdef0123456",
    createdDate: "",
    lastUpdatedDate: "2026-04-19T14:26:24.980Z",
    createdBy: null,
    lastUpdatedBy: "",
    language: null,
    organizationIds: [],
    tags: [],
    service: "Storage",
    method: "GET",
    endpoint: "/Storage/Download/{fileId}",
    description: "Download a file from the storage bucket by file ID.",
    isCaptchaRequired: false,
    captchaProvider: "",
    isMfaRequired: false,
    mfaType: "",
  },
  {
    itemId: "0123cdef-0123-4567-89ab-cdef01234567",
    createdDate: "",
    lastUpdatedDate: "2026-04-19T14:26:24.980Z",
    createdBy: null,
    lastUpdatedBy: "",
    language: null,
    organizationIds: [],
    tags: [],
    service: "Storage",
    method: "DELETE",
    endpoint: "/Storage/Delete/{fileId}",
    description: "Permanently delete a file from the storage bucket.",
    isCaptchaRequired: false,
    captchaProvider: "",
    isMfaRequired: false,
    mfaType: "",
  },
  {
    itemId: "1234def0-1234-5678-9abc-def012345678",
    createdDate: "",
    lastUpdatedDate: "2026-04-19T14:26:24.980Z",
    createdBy: null,
    lastUpdatedBy: "",
    language: null,
    organizationIds: [],
    tags: [],
    service: "Storage",
    method: "GET",
    endpoint: "/Storage/ListBuckets",
    description: "List all storage buckets for the project.",
    isCaptchaRequired: false,
    captchaProvider: "",
    isMfaRequired: false,
    mfaType: "",
  },
  {
    itemId: "2345ef01-2345-6789-abcd-ef0123456789",
    createdDate: "",
    lastUpdatedDate: "2026-04-19T14:26:24.980Z",
    createdBy: null,
    lastUpdatedBy: "",
    language: null,
    organizationIds: [],
    tags: [],
    service: "Storage",
    method: "PUT",
    endpoint: "/Storage/UpdateMetadata/{fileId}",
    description: "Update metadata for an existing file.",
    isCaptchaRequired: false,
    captchaProvider: "",
    isMfaRequired: false,
    mfaType: "",
  },
  {
    itemId: "3456f012-3456-789a-bcde-f01234567890",
    createdDate: "",
    lastUpdatedDate: "2026-04-19T14:26:24.980Z",
    createdBy: null,
    lastUpdatedBy: "",
    language: null,
    organizationIds: [],
    tags: [],
    service: "Storage",
    method: "POST",
    endpoint: "/Storage/CreateBucket",
    description: "Create a new storage bucket.",
    isCaptchaRequired: false,
    captchaProvider: "",
    isMfaRequired: false,
    mfaType: "",
  },
  {
    itemId: "4567f123-4567-89ab-cdef-012345678901",
    createdDate: "",
    lastUpdatedDate: "2026-04-19T14:26:24.980Z",
    createdBy: null,
    lastUpdatedBy: "",
    language: null,
    organizationIds: [],
    tags: [],
    service: "Storage",
    method: "DELETE",
    endpoint: "/Storage/DeleteBucket/{bucketId}",
    description: "Delete a storage bucket and all its contents.",
    isCaptchaRequired: false,
    captchaProvider: "",
    isMfaRequired: false,
    mfaType: "",
  },
  {
    itemId: "5678a234-5678-9abc-def0-123456789012",
    createdDate: "",
    lastUpdatedDate: "2026-04-19T14:26:24.980Z",
    createdBy: null,
    lastUpdatedBy: "",
    language: null,
    organizationIds: [],
    tags: [],
    service: "Storage",
    method: "GET",
    endpoint: "/Storage/ListObjects/{bucketId}",
    description: "List all objects in a specific storage bucket.",
    isCaptchaRequired: false,
    captchaProvider: "",
    isMfaRequired: false,
    mfaType: "",
  },
  {
    itemId: "6789b345-6789-abcd-ef01-234567890123",
    createdDate: "",
    lastUpdatedDate: "2026-04-19T14:26:24.980Z",
    createdBy: null,
    lastUpdatedBy: "",
    language: null,
    organizationIds: [],
    tags: [],
    service: "Storage",
    method: "POST",
    endpoint: "/Storage/CopyObject",
    description: "Copy an object between buckets or within the same bucket.",
    isCaptchaRequired: false,
    captchaProvider: "",
    isMfaRequired: false,
    mfaType: "",
  },
  {
    itemId: "789ac456-789a-bcde-f012-345678901234",
    createdDate: "",
    lastUpdatedDate: "2026-04-19T14:26:24.980Z",
    createdBy: null,
    lastUpdatedBy: "",
    language: null,
    organizationIds: [],
    tags: [],
    service: "Storage",
    method: "POST",
    endpoint: "/Storage/GeneratePresignedUrl",
    description: "Generate a pre-signed URL for temporary file access.",
    isCaptchaRequired: false,
    captchaProvider: "",
    isMfaRequired: false,
    mfaType: "",
  },
  {
    itemId: "89abd567-89ab-cdef-0123-456789012345",
    createdDate: "",
    lastUpdatedDate: "2026-04-19T14:26:24.980Z",
    createdBy: null,
    lastUpdatedBy: "",
    language: null,
    organizationIds: [],
    tags: [],
    service: "Storage",
    method: "GET",
    endpoint: "/Storage/GetObjectInfo/{fileId}",
    description: "Get detailed information about a specific file.",
    isCaptchaRequired: false,
    captchaProvider: "",
    isMfaRequired: false,
    mfaType: "",
  },
  {
    itemId: "9abce678-9abc-def0-1234-567890123456",
    createdDate: "",
    lastUpdatedDate: "2026-04-19T14:26:24.980Z",
    createdBy: null,
    lastUpdatedBy: "",
    language: null,
    organizationIds: [],
    tags: [],
    service: "Storage",
    method: "PUT",
    endpoint: "/Storage/SetBucketPolicy/{bucketId}",
    description: "Set access policy for a storage bucket.",
    isCaptchaRequired: false,
    captchaProvider: "",
    isMfaRequired: false,
    mfaType: "",
  },
  {
    itemId: "abcdf789-abcd-ef01-2345-678901234567",
    createdDate: "",
    lastUpdatedDate: "2026-04-19T14:26:24.980Z",
    createdBy: null,
    lastUpdatedBy: "",
    language: null,
    organizationIds: [],
    tags: [],
    service: "Storage",
    method: "PATCH",
    endpoint: "/Storage/UpdateBucketCORS/{bucketId}",
    description: "Update CORS configuration for a storage bucket.",
    isCaptchaRequired: false,
    captchaProvider: "",
    isMfaRequired: false,
    mfaType: "",
  },
  {
    itemId: "bcdef890-bcde-f012-3456-789012345678",
    createdDate: "",
    lastUpdatedDate: "2026-04-19T14:26:24.980Z",
    createdBy: null,
    lastUpdatedBy: "",
    language: null,
    organizationIds: [],
    tags: [],
    service: "Storage",
    method: "POST",
    endpoint: "/Storage/MultipartUpload/Init",
    description: "Initialize a multipart upload session.",
    isCaptchaRequired: false,
    captchaProvider: "",
    isMfaRequired: false,
    mfaType: "",
  },
  {
    itemId: "cdef0901-cdef-0123-4567-890123456789",
    createdDate: "",
    lastUpdatedDate: "2026-04-19T14:26:24.980Z",
    createdBy: null,
    lastUpdatedBy: "",
    language: null,
    organizationIds: [],
    tags: [],
    service: "Storage",
    method: "POST",
    endpoint: "/Storage/MultipartUpload/Complete",
    description: "Complete a multipart upload and assemble the file.",
    isCaptchaRequired: false,
    captchaProvider: "",
    isMfaRequired: false,
    mfaType: "",
  },
];

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
  const { data, isLoading } = useGetApiEndpoints({ projectKey: tenantId });
  const { mutateAsync: updateEndpoint } = useUpdateApiEndpoint();
  const { mutateAsync: bulkUpdate } = useBulkUpdateApiEndpoints();
  const { mutateAsync: removeEndpoints } = useRemoveApiEndpoints();

  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [removeConfirmOpen, setRemoveConfirmOpen] = useState(false);

  // Use API data when available, else fall back to mock
  const endpoints = data?.endpoints ?? MOCK_ENDPOINTS;

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

  // ── Toggle handlers (single endpoint) ─────────────────────────────────────
  const handleToggleMfa = useCallback(
    async (ep: IApiEndpoint, value: boolean) => {
      try {
        await updateEndpoint({
          projectKey: tenantId,
          itemId: ep.itemId,
          isMfaRequired: value,
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
          isCaptchaRequired: value,
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
        await bulkUpdate({ projectKey: tenantId, itemIds: ids, isMfaRequired: value });
        showSuccessToast({ description: `MFA ${value ? "enabled" : "disabled"} for ${ids.length} endpoints` });
      } catch {
        showErrorToast({ errors: "Failed to bulk update MFA" });
      }
    },
    [tenantId, bulkUpdate],
  );

  const handleBulkGroupCaptcha = useCallback(
    async (ids: string[], value: boolean) => {
      try {
        await bulkUpdate({ projectKey: tenantId, itemIds: ids, isCaptchaRequired: value });
        showSuccessToast({ description: `Captcha ${value ? "enabled" : "disabled"} for ${ids.length} endpoints` });
      } catch {
        showErrorToast({ errors: "Failed to bulk update Captcha" });
      }
    },
    [tenantId, bulkUpdate],
  );

  // ── Bulk bar actions ──────────────────────────────────────────────────────
  const selectedArray = useMemo(() => Array.from(selectedIds), [selectedIds]);

  const handleBulkMfa = useCallback(async () => {
    try {
      await bulkUpdate({ projectKey: tenantId, itemIds: selectedArray, isMfaRequired: true });
      showSuccessToast({ description: `MFA enabled for ${selectedArray.length} endpoints` });
      clearSelection();
    } catch {
      showErrorToast({ errors: "Failed to enable MFA" });
    }
  }, [tenantId, selectedArray, bulkUpdate, clearSelection]);

  const handleBulkCaptcha = useCallback(async () => {
    try {
      await bulkUpdate({ projectKey: tenantId, itemIds: selectedArray, isCaptchaRequired: true });
      showSuccessToast({ description: `Captcha enabled for ${selectedArray.length} endpoints` });
      clearSelection();
    } catch {
      showErrorToast({ errors: "Failed to enable Captcha" });
    }
  }, [tenantId, selectedArray, bulkUpdate, clearSelection]);

  const handleBulkRemove = useCallback(async () => {
    try {
      await removeEndpoints({ projectKey: tenantId, itemIds: selectedArray });
      showSuccessToast({ description: `${selectedArray.length} endpoints removed` });
      clearSelection();
      setRemoveConfirmOpen(false);
    } catch {
      showErrorToast({ errors: "Failed to remove endpoints" });
    }
  }, [tenantId, selectedArray, removeEndpoints, clearSelection]);

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
        onRemove={() => setRemoveConfirmOpen(true)}
        onClear={clearSelection}
      />

      {/* Remove confirmation */}
      <Dialog open={removeConfirmOpen} onOpenChange={setRemoveConfirmOpen}>
        <ConfirmationModal
          data={{
            dialogTitle: "Remove Endpoints",
            dialogSubtitle: `Are you sure you want to remove ${selectedIds.size} endpoint${selectedIds.size !== 1 ? "s" : ""}? This action cannot be undone.`,
            confirmButton: "Remove",
            cancelButton: "Cancel",
          }}
          onConfirm={handleBulkRemove}
          onCancel={() => setRemoveConfirmOpen(false)}
        />
      </Dialog>
    </main>
  );
}
