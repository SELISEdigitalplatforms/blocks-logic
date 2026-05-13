import { API_BASES } from "./endpoint.constant";

const PROJECT_SUBPATH = "/Project";
export const PROJECT_ENDPOINTS = {
  GETS: `${API_BASES.IDP}${PROJECT_SUBPATH}/Gets`,
  GET: `${API_BASES.IDP}${PROJECT_SUBPATH}/Get`,

  DISABLE: `${API_BASES.IDENTIFIER}${PROJECT_SUBPATH}/Disable`,

}