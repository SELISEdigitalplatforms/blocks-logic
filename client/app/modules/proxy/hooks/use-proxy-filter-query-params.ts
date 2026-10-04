import { parseAsInteger, parseAsString, useQueryStates } from "nuqs";
import { PROXY_PAGE_SIZE } from "../constants";

export const useProxyFilterQueryParams = () => {
  const [queryParams, setQueryParams] = useQueryStates(
    {
      search: parseAsString.withDefault(""),
      isActive: parseAsString.withDefault("all"),
      page: parseAsInteger.withDefault(0),
      pageSize: parseAsInteger.withDefault(PROXY_PAGE_SIZE),
    },
    { clearOnDefault: true },
  );
  return { queryParams, setQueryParams };
};
