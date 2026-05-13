import { parseAsInteger, parseAsString, useQueryStates } from "nuqs";

export const useWorkflowFilterQueryParams = () => {
  const [queryParams, setQueryParams] = useQueryStates(
    {
      search: parseAsString.withDefault(""),
      isActive: parseAsString.withDefault("all"),
      page: parseAsInteger.withDefault(0),
      pageSize: parseAsInteger.withDefault(10),
    },
    { clearOnDefault: true },
  );
  return { queryParams, setQueryParams };
};
