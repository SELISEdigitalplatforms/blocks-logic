import { useQuery } from "@tanstack/react-query";
import { secretService } from "@/services/secret.service";
import { SecretListParams } from "@/models/secret";

export const VARIABLE_CATALOG_QUERY_KEY = ["variable-catalog"] as const;

/**
 * The tenant's platform configuration variables, from the shared `GET /api/Secret/GetAll`.
 *
 * Shared rather than per-module: Proxy, Functions and anything added later read the same endpoint,
 * and one query key means a picker opened in one place is already warm in the next.
 *
 * Fetched unfiltered on purpose. Search and tag filtering happen over the rows in the browser —
 * the only way to offer tag options at all, since there is no tag endpoint — and holding the whole
 * catalog is also what lets a stored reference be resolved back to a name for display.
 *
 * Identity and tags only. A value is resolved server-side at execution time and never reaches the
 * console, so nothing downstream of this hook is capable of displaying one.
 */
export const useVariableCatalog = (
  params: SecretListParams = {},
  options: { enabled?: boolean } = {},
) =>
  useQuery({
    queryKey: [...VARIABLE_CATALOG_QUERY_KEY, params],
    queryFn: () => secretService.getAll(params),
    staleTime: 60_000,
    // A field whose caller already supplies the rows must not fetch them again.
    enabled: options.enabled ?? true,
  });
