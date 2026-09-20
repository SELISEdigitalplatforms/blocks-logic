/**
 * The tenant's platform secrets, read-only, shared by every module that offers a secret picker
 * (Proxy's `{{$VAR.name}}` menu, Workflow, ...).
 *
 * `SeliseBlocks.Secrets.OS` is an in-process NuGet library with **no HTTP surface of its own**, so the
 * console cannot call it directly. `GET /api/Secret/GetAll` is the one control-plane wrapper the logic
 * API exposes over the in-process `ISecretService`. Identity and tags only — there is no `values` call:
 * plaintext is resolved server-side at execution time and never reaches the console.
 */

/** One row of `GET /api/Secret/GetAll`, mapped for the UI. */
export type SecretListItem = {
  id: string;
  name: string;
  tags: string[];
};

/** One row exactly as the API returns it. */
export type SecretListItemDto = {
  id: string;
  name: string;
  tags?: string[] | null;
};

/** Response envelope of `GET /api/Secret/GetAll` (`SecretListResponse`). */
export type SecretListResponseDto = {
  data: SecretListItemDto[] | null;
  totalCount: number;
};

/** Filters of `GET /api/Secret/GetAll`. All optional; an omitted field is not a filter. */
export type SecretListParams = {
  /** Exact (case-insensitive) secret name. */
  name?: string;
  /** A single tag key the secret must carry. */
  tag?: string;
  /** Case-insensitive substring match on the secret name. */
  search?: string;
};
