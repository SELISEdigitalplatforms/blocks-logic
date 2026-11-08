import { beforeEach, describe, expect, it, vi } from "vitest";

const http = vi.hoisted(() => ({
  logicService: { get: vi.fn().mockResolvedValue([]) },
}));

vi.mock("@/lib/http-client", () => ({ serviceInstances: http }));

import { actionsService } from "./actions.service";

beforeEach(() => {
  vi.clearAllMocks();
});

// The contract with the server's ActionsController: each editor reads its own filtered list.
describe("actionsService", () => {
  it("the workflow node reads Actions/GetForWorkflow", async () => {
    await actionsService.getForWorkflow();
    expect(http.logicService.get).toHaveBeenCalledWith(expect.stringMatching(/\/Actions\/GetForWorkflow$/));
  });

  it("the function Trigger section reads Actions/GetForFunction", async () => {
    await actionsService.getForFunction();
    expect(http.logicService.get).toHaveBeenCalledWith(expect.stringMatching(/\/Actions\/GetForFunction$/));
  });
});
