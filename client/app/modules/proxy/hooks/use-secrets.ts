import { useQuery } from "@tanstack/react-query";
import { PROXY_QUERY_KEY } from "../constants";
import { secretService, usableSecrets } from "../services";
import { SecretListParams } from "../types";

const SECRETS_QUERY_KEY = [...PROXY_QUERY_KEY, "secrets"] as const;

/**
 * The tenant's configuration variables for the `{{$VAR.name}}` picker. Filtered to the types a
 * proxy can actually resolve (`service` / `both`) so an unusable `api`-typed secret is never
 * offered. Cached for 5 minutes — the list changes rarely and the picker tolerates staleness.
 */
export const useSecrets = (params: SecretListParams = {}) =>
  useQuery({
    queryKey: [...SECRETS_QUERY_KEY, params],
    queryFn: async () => usableSecrets(await secretService.getAll(params)),
    staleTime: 5 * 60 * 1000,
  });

export const useSecretTags = () =>
  useQuery({
    queryKey: [...SECRETS_QUERY_KEY, "tags"],
    queryFn: () => secretService.getTags(),
    staleTime: 5 * 60 * 1000,
  });
