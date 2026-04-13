import { http } from "@/lib/http-client";
import { IGetProjectPayload, IGetProjectResponse, IProjectGroup } from "@/models/project.model";

export class ProjectService {
  getProjects(page = 0, pageSize = 100, tenantGroupId = ""): Promise<IProjectGroup[]> {
    const url = `/identifier/v1/Project/Gets?page=${page}&pageSize=${pageSize}&tenantGroupId=${tenantGroupId}`;
    return http.get(url);
  }

  getProject(payload: IGetProjectPayload): Promise<IGetProjectResponse> {
    const url = `/identifier/v1/Project/Get?projectId=${payload.projectId}`;
    return http.get(url);
  }
}

export const projectService = new ProjectService();