import { PermissionService } from "./permission.service";

class IAMService {
  constructor(
    public permission: PermissionService,
  ) {}
}

export const iamService = new IAMService(new PermissionService());
