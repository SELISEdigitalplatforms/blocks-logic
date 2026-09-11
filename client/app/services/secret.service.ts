import { serviceInstances } from "@/lib/http-client";
import { SecretListItem, SecretListItemDto, SecretListParams, SecretListResponseDto } from "@/models/secret";

/**
 * The tenant's platform secrets, read-only. Not owned by any one module — Proxy's `{{$VAR.name}}`
 * picker, Workflow, and anything added later all read this same endpoint.
 *
 * One call is all there is: the server already narrows the list to the platform secrets a backend
 * module can resolve, so there is no type to filter on here and no separate tag endpoint — tag options
 * come from the rows themselves via {@link secretTagOptions}.
 */
export const SECRET_ENDPOINTS = {
  GETS: "/api/Secret/GetAll",
} as const;

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
  id: dto.id,
  name: dto.name,
  tags: Array.isArray(dto.tags) ? dto.tags : [],
});

/** The distinct tags present in a secret list, sorted — the picker's tag filter options. */
export const secretTagOptions = (items: SecretListItem[]): string[] =>
  [...new Set(items.flatMap((item) => item.tags))].sort((a, b) => a.localeCompare(b));

export class SecretService {
  private readonly logicHttpClient = serviceInstances.logicService;

  endpoints = SECRET_ENDPOINTS;

  getAll = async (params: SecretListParams = {}): Promise<SecretListItem[]> => {
    const response = await this.logicHttpClient.get<SecretListResponseDto>(
      `${SECRET_ENDPOINTS.GETS}${buildQuery({
        name: params.name?.trim() || undefined,
        tag: params.tag?.trim() || undefined,
        search: params.search?.trim() || undefined,
      })}`,
    );
    return (response.data ?? []).map(mapSecretListItemDto);
  };
}

export const secretService = new SecretService();
