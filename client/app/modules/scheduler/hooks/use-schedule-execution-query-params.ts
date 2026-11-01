import { parseAsInteger, parseAsString, useQueryStates } from "nuqs";

/** The Executions tab's URL state. `from`/`to` are calendar days (`yyyy-MM-dd`), not instants. */
export const useScheduleExecutionQueryParams = () => {
  const [queryParams, setQueryParams] = useQueryStates(
    {
      outcome: parseAsString.withDefault("all"),
      from: parseAsString.withDefault(""),
      to: parseAsString.withDefault(""),
      page: parseAsInteger.withDefault(0),
      pageSize: parseAsInteger.withDefault(10),
    },
    { clearOnDefault: true },
  );
  return { queryParams, setQueryParams };
};
