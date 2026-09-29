import { useQuery } from "@tanstack/react-query";
import { iamService } from "@/modules/workflow/services/iam.service";

export type AccessOption = { label: string; value: string };

export const IAM_ROLES_QUERY_KEY = ["iam", "roles"] as const;
export const IAM_PERMISSIONS_QUERY_KEY = ["iam", "permissions"] as const;

/**
 * The tenant's roles as `{ label: name, value: slug }` for the "Who can call it" picker. Same IAM
 * source the workflow webhook trigger uses, so a role picked here is the same slug the token carries.
 * Cached for 5 minutes — the list changes rarely and the picker tolerates staleness.
 */
export const useIamRoles = () =>
  useQuery({
    queryKey: IAM_ROLES_QUERY_KEY,
    queryFn: async (): Promise<AccessOption[]> => {
      const res = await iamService.getRoles();
      return (res?.data ?? [])
        .filter((role) => typeof role?.slug === "string" && role.slug.length > 0)
        .map((role) => ({ label: role.name || role.slug, value: role.slug }));
    },
    staleTime: 5 * 60 * 1000,
    retry: false,
  });

/** The tenant's permissions as `{ label: name, value: resource }` — the resource key is what the token carries. */
export const useIamPermissions = () =>
  useQuery({
    queryKey: IAM_PERMISSIONS_QUERY_KEY,
    queryFn: async (): Promise<AccessOption[]> => {
      const res = await iamService.getPermissions({});
      return (res?.data ?? [])
        .filter(
          (permission) =>
            typeof permission?.resource === "string" && permission.resource.length > 0,
        )
        .map((permission) => ({
          label: permission.name || permission.resource,
          value: permission.resource,
        }));
    },
    staleTime: 5 * 60 * 1000,
    retry: false,
  });
