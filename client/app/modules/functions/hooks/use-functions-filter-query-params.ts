import { parseAsInteger, parseAsString, useQueryStates } from "nuqs";

export const useFunctionsFilterQueryParams = () => {
  const [queryParams, setQueryParams] = useQueryStates(
    {
      search: parseAsString.withDefault(""),
      status: parseAsString.withDefault(""),
      sort: parseAsString.withDefault(""),
      page: parseAsInteger.withDefault(0),
      pageSize: parseAsInteger.withDefault(10),
    },
    { clearOnDefault: true },
  );
  return { queryParams, setQueryParams };
};
