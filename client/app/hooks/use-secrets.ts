import { useQuery } from "@tanstack/react-query";
import { secretService } from "@/services/secret.service";
import { SecretListParams } from "@/models/secret";

const SECRETS_QUERY_KEY = ["secrets", "variable-picker"] as const;

/**
 * The tenant's platform secrets for the `{{$VAR.name}}` picker (proxy config, workflow node inputs),
 * from the shared `GET /api/Secret/GetAll`. The server already narrows the list to the secrets that
 * can actually be resolved, so nothing is filtered here. Every picker on a page shares one fetch. Cached for 5 minutes — the list changes rarely and the picker
 * tolerates staleness.
 */
export const useSecrets = (params: SecretListParams = {}) =>
  useQuery({
    queryKey: [...SECRETS_QUERY_KEY, params],
    queryFn: () => secretService.getAll(params),
    staleTime: 5 * 60 * 1000,
  });
