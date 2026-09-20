import { useQuery } from "@tanstack/react-query";
import { secretService } from "@/services/secret.service";
import { SecretListParams } from "@/models/secret";
import { PROXY_QUERY_KEY } from "../constants";

const SECRETS_QUERY_KEY = [...PROXY_QUERY_KEY, "secrets"] as const;

/**
 * The tenant's platform secrets for the `{{$VAR.name}}` picker, from the shared
 * `GET /api/Secret/GetAll`. The server already narrows the list to the secrets a proxy can actually
 * resolve, so nothing is filtered here. Cached for 5 minutes — the list changes rarely and the picker
 * tolerates staleness.
 */
export const useSecrets = (params: SecretListParams = {}) =>
  useQuery({
    queryKey: [...SECRETS_QUERY_KEY, params],
    queryFn: () => secretService.getAll(params),
    staleTime: 5 * 60 * 1000,
  });
