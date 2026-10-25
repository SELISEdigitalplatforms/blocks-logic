import { useQuery } from "@tanstack/react-query";
import { functionService } from "../services/function.service";
import { FUNCTIONS_QUERY_KEY } from "./use-functions";

export const useGetVersions = (functionId: string | undefined, pageNumber = 0, pageSize = 20) => {
  return useQuery({
    queryKey: [FUNCTIONS_QUERY_KEY, "versions", functionId, pageNumber, pageSize],
    queryFn: () => functionService.getVersions(functionId!, pageNumber, pageSize),
    enabled: !!functionId,
  });
};

export const useGetVersionSource = (
  functionId: string | undefined,
  versionId: string | undefined,
) => {
  return useQuery({
    queryKey: [FUNCTIONS_QUERY_KEY, "versions", functionId, "source", versionId],
    queryFn: () => functionService.getVersionSource(functionId!, versionId!),
    enabled: !!functionId && !!versionId,
  });
};

/** The deploy still building for this function, read once on load so a reload keeps showing it. */
export const useGetPendingDeploy = (functionId: string | undefined) => {
  return useQuery({
    queryKey: [FUNCTIONS_QUERY_KEY, "pending-deploy", functionId],
    queryFn: () => functionService.getPendingDeploy(functionId!),
    enabled: !!functionId,
    refetchOnWindowFocus: false,
  });
};

export const useGetBuild = (buildId: string | undefined) => {
  return useQuery({
    queryKey: [FUNCTIONS_QUERY_KEY, "build", buildId],
    queryFn: () => functionService.getBuild(buildId!),
    enabled: !!buildId,
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      return status === "Queued" || status === "Building" ? 2000 : false;
    },
  });
};
