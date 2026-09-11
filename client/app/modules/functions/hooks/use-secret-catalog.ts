import { useQuery } from "@tanstack/react-query";
import { secretService } from "@/services/secret.service";
import { SecretListParams } from "@/models/secret";
import { FUNCTIONS_QUERY_KEY } from "./use-functions";

/**
 * The tenant's platform secrets for the `{{secret.NAME}}` picker, from the shared
 * `GET /api/Secret/GetAll` rather than a Functions-owned wrapper: the endpoint is deliberately
 * module-agnostic, and the server already narrows the list to the secrets a backend module can
 * resolve, so nothing is filtered here. Names only — a value is resolved server-side at execution
 * time and never reaches the console.
 */
export const useGetSecretCatalog = (params: SecretListParams = {}) => {
  return useQuery({
    queryKey: [FUNCTIONS_QUERY_KEY, "secrets", params],
    queryFn: () => secretService.getAll(params),
    staleTime: 60_000,
  });
};
