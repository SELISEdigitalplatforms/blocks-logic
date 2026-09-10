import { serviceInstances } from "@/lib/http-client";
import {
  SecretListItem,
  SecretListItemDto,
  SecretListParams,
  SecretListResponseDto,
} from "../types";

/**
 * The tenant's configuration variables, read-only, for the `{{$VAR.name}}` picker.
 *
 * `SeliseBlocks.Secrets.OS` is an in-process NuGet library with **no HTTP surface of its own**, so the
 * console cannot call it directly. These routes are the thin control-plane wrapper the logic API exposes
 * over the in-process `ISecretService` (`ProxyController.Variables` / `.VariableTags`). Names / ids / type
 * / tags only — there is no `values` call, plaintext is resolved server-side on the forward / Test path
 * and never reaches the console.
 */
export const SECRET_ENDPOINTS = {
  GETS: "/api/Proxy/Variables",
  TAGS: "/api/Proxy/VariableTags",
} as const;

/** Types a proxy can actually resolve at forward time (an `api`-typed secret is out of reach). */
const USABLE_TYPES = new Set(["service", "both"]);

const buildQuery = (params: Record<string, string | number | undefined>) => {
  const search = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== "") {
      search.append(key, String(value));
    }
  });
  const query = search.toString();
  return query ? `?${query}` : "";
};

export const mapSecretListItemDto = (dto: SecretListItemDto): SecretListItem => ({
  id: dto.secretId,
  name: dto.name,
  type: (dto.type ?? "").toLowerCase(),
  tags: Array.isArray(dto.tags) ? dto.tags : [],
});

/** Keeps only the entries a proxy can resolve (`service` / `both`). */
export const usableSecrets = (items: SecretListItem[]): SecretListItem[] =>
  items.filter((item) => USABLE_TYPES.has(item.type));

export class SecretService {
  private readonly logicHttpClient = serviceInstances.logicService;

  endpoints = SECRET_ENDPOINTS;

  getAll = async (params: SecretListParams = {}): Promise<SecretListItem[]> => {
    const response = await this.logicHttpClient.get<SecretListResponseDto>(
      `${SECRET_ENDPOINTS.GETS}${buildQuery({
        search: params.search?.trim() || undefined,
      })}`,
    );
    return (response.data ?? []).map(mapSecretListItemDto);
  };

  getTags = async (): Promise<string[]> => {
    const response = await this.logicHttpClient.get<{ key: string; label: string }[]>(
      SECRET_ENDPOINTS.TAGS,
    );
    return (response ?? []).map((tag) => tag.key);
  };
}

export const secretService = new SecretService();
