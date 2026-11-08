import { useQuery } from "@tanstack/react-query";
import { actionsService } from "../services/actions.service";

export const ACTIONS_QUERY_KEY = ["actions"] as const;

/** Actions with the Function switch on: the function Trigger section's options. */
export const useGetFunctionActions = () => {
  return useQuery({
    queryKey: [...ACTIONS_QUERY_KEY, "function"],
    queryFn: actionsService.getForFunction,
  });
};
