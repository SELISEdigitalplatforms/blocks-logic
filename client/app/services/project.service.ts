import { API_BASES } from "@/constants/endpoint.constant";
import { http } from "@/lib/http-client";
// import { PROJECT_ENDPOINTS } from "@blocks-identifier/constants/endpoint.constant";
import { IGetProjectPayload, IGetProjectResponse, IProjectGroup } from "@/models/project.model";

const PROJECT_SUBPATH = "/Project";
export const PROJECT_ENDPOINTS = {
  GETS: `${API_BASES.IDENTIFIER}${PROJECT_SUBPATH}/Gets`,
  GET: `${API_BASES.IDENTIFIER}${PROJECT_SUBPATH}/Get`,
}
export class ProjectService {
  getProjects(page = 0, pageSize = 100, tenantGroupId = ""): Promise<IProjectGroup[]> {
    const url = `${PROJECT_ENDPOINTS.GETS}?page=${page}&pageSize=${pageSize}&tenantGroupId=${tenantGroupId}`;
    return http.get(url);
  }

  getProject(payload: IGetProjectPayload): Promise<IGetProjectResponse> {
    const url = `${PROJECT_ENDPOINTS.GET}?projectId=${payload.projectId}`;
    return http.get(url);
  }
}

export const projectService = new ProjectService();