import { useQuery } from "@tanstack/react-query";
import { functionService } from "../services/function.service";
import { FUNCTIONS_QUERY_KEY } from "./use-functions";

export const useGetSecretCatalog = () => {
  return useQuery({
    queryKey: [FUNCTIONS_QUERY_KEY, "secret-catalog"],
    queryFn: () => functionService.getSecretCatalog(),
    staleTime: 60_000,
  });
};
