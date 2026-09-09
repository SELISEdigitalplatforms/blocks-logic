import { serviceInstances } from "@/lib/http-client";
import {
  SecretListItem,
  SecretListItemDto,
  SecretListParams,
  SecretListResponseDto,
} from "../types";

/**
 * Blocks OS Secret management, read-only. The proxy console reads the tenant's variable list
 * straight from the package's own routes (via the logic HTTP client) to populate the
 * `{{$VAR.name}}` picker. There is no `values` call here — plaintext is server-only.
 */
export const SECRET_ENDPOINTS = {
  GETS: "/api/Secrets/gets",
  TAGS: "/api/Secrets/tags",
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
        tags: params.tags?.length ? params.tags.join(",") : undefined,
        pageSize: params.pageSize ?? 200,
        pageNumber: params.pageNumber ?? 0,
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
