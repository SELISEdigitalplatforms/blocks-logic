import { useQuery } from "@tanstack/react-query";
import { functionService } from "../services/function.service";
import { FUNCTIONS_QUERY_KEY } from "./use-functions";

export interface IGetFunctionOptions {
  functionId?: string;
  enabled?: boolean;
}

export const useGetFunction = ({ functionId, enabled = true }: IGetFunctionOptions) => {
  return useQuery({
    queryKey: [FUNCTIONS_QUERY_KEY, "detail", functionId],
    queryFn: () => functionService.getFunction(functionId!),
    enabled: enabled && !!functionId,
  });
};
