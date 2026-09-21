import { getLogicBaseUrl } from "@/lib/logic-base-url";

export const getApiPath = (_servicePath: string): string => {
  return "/api";
};

export const getApiUrl = (_servicePath: string, endpoint: string): string => {
  const baseUrl = getLogicBaseUrl();
  return `${baseUrl}/api/${endpoint}`;
};
