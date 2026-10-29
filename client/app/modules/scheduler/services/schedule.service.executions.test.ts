import { describe, expect, it, vi } from "vitest";
import { scheduleService } from "./schedule.service";
import { SCHEDULER_ENDPOINTS } from "../constants/endpoint.constant";

describe("scheduleService executions", () => {
  it("posts the list and detail requests to their endpoints", async () => {
    const client = (scheduleService as unknown as { logicHttpClient: { post: (url: string, body: unknown) => Promise<unknown> } })
      .logicHttpClient;
    const post = vi.spyOn(client, "post").mockResolvedValue({ isSuccess: true });

    const list = { scheduleId: "s1", pageNumber: 0, pageSize: 10 };
    await scheduleService.getScheduleExecutions(list);
    await scheduleService.getScheduleExecution({ scheduleId: "s1", executionId: "e1" });

    expect(SCHEDULER_ENDPOINTS.GET_EXECUTIONS).toMatch(/\/Scheduler\/GetScheduleExecutions$/);
    expect(SCHEDULER_ENDPOINTS.GET_EXECUTION).toMatch(/\/Scheduler\/GetScheduleExecution$/);
    expect(post).toHaveBeenNthCalledWith(1, SCHEDULER_ENDPOINTS.GET_EXECUTIONS, list);
    expect(post).toHaveBeenNthCalledWith(2, SCHEDULER_ENDPOINTS.GET_EXECUTION, { scheduleId: "s1", executionId: "e1" });
  });
});
