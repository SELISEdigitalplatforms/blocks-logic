import { beforeEach, describe, expect, it, vi } from "vitest";

// A single shared HTTP-client double stands in for every service instance.
const http = vi.hoisted(() => {
  const make = () => ({
    get: vi.fn().mockResolvedValue({ ok: true }),
    post: vi.fn().mockResolvedValue({ ok: true }),
    put: vi.fn().mockResolvedValue({ ok: true }),
    delete: vi.fn().mockResolvedValue({ ok: true }),
  });
  return {
    logicService: make(),
    agentsService: make(),
    dataService: make(),
    iamService: make(),
  };
});

vi.mock("@/lib/http-client", () => ({ serviceInstances: http }));

import { workflowService } from "./workflow.service";
import { emailService } from "./email.services";
import { agentService } from "./agent.service";
import { dataService } from "./data.service";
import { languageManagerService } from "./language.manager.service";
import { authClientService } from "./iam.service";

beforeEach(() => {
  vi.clearAllMocks();
});

describe("workflowService", () => {
  it("posts to GetAll for getWorkflows", async () => {
    await workflowService.getWorkflows({ page: 1 } as never);
    expect(http.logicService.post).toHaveBeenCalledWith(
      expect.stringContaining("/Workflow/GetAll"),
      { page: 1 },
    );
  });

  it("gets a workflow by id", async () => {
    await workflowService.getWorkflowById({ id: "w1" } as never);
    expect(http.logicService.get).toHaveBeenCalledWith(
      expect.stringContaining("WorkflowId=w1"),
    );
  });

  it("creates, duplicates and updates workflows", async () => {
    await workflowService.createWorkflow({ a: 1 } as never);
    expect(http.logicService.post).toHaveBeenCalledWith(
      expect.stringContaining("/Create"),
      { a: 1 },
    );
    await workflowService.duplicateWorkflow({ a: 1 } as never);
    expect(http.logicService.post).toHaveBeenCalledWith(
      expect.stringContaining("/Duplicate"),
      { a: 1 },
    );
    await workflowService.updateWorkflow({ a: 1 } as never);
    expect(http.logicService.put).toHaveBeenCalledWith(
      expect.stringContaining("/Update"),
      { a: 1 },
    );
  });

  it("deletes a workflow by id", async () => {
    await workflowService.deleteWorkflow({ id: "w2" } as never);
    expect(http.logicService.delete).toHaveBeenCalledWith(
      expect.stringContaining("id=w2"),
    );
  });

  it("reads executions and a single execution", async () => {
    await workflowService.getWorkflowExecutions({ workflowId: "w1" } as never);
    expect(http.logicService.get).toHaveBeenCalledWith(
      expect.stringContaining("WorkflowId=w1"),
    );
    await workflowService.getWorkflowExecutionById({
      executionId: "e1",
    } as never);
    expect(http.logicService.get).toHaveBeenCalledWith(
      expect.stringContaining("ExecutionId=e1"),
    );
  });

  it("covers version, publish, restore and listener endpoints", async () => {
    await workflowService.createWorkflowVersion({} as never);
    await workflowService.getWorkflowVersions({} as never);
    await workflowService.publishWorkflow({} as never);
    await workflowService.publishWorkflowNewVersion({} as never);
    await workflowService.unpublishWorkflow({} as never);
    await workflowService.restoreWorkflow({} as never);
    await workflowService.getWorkflowByVersion({} as never);
    await workflowService.updateWorkflowVersion({} as never);
    await workflowService.stepExecute({} as never);
    await workflowService.triggerListener({} as never);
    // 10 posts above, plus none of the gets
    expect(http.logicService.post).toHaveBeenCalledTimes(10);
  });

  it("gets the last successful execution", async () => {
    await workflowService.getLastSuccessfulExecution({
      workflowId: "w9",
    } as never);
    expect(http.logicService.get).toHaveBeenCalledWith(
      expect.stringContaining("workflowId=w9"),
    );
  });
});

describe("emailService", () => {
  it("fetches inbound-capable email configs with paging", async () => {
    await emailService.fetchEmailConfigs("pk", 0, 50);
    expect(http.logicService.get).toHaveBeenCalledWith(
      expect.stringContaining("pageNumber=1"),
      undefined,
      { absoluteUrl: true },
    );
  });

  it("fetches email templates", async () => {
    await emailService.fetchEmailTemplates(0, 10, "pk", "", "Name", false, "", "");
    expect(http.logicService.get).toHaveBeenCalledWith(
      expect.stringContaining("projectKey=pk"),
      undefined,
      { absoluteUrl: true },
    );
  });
});

describe("agentService", () => {
  it("posts the agent query payload", async () => {
    await agentService.getAgents({
      limit: 10,
      offset: 0,
      project_key: "pk",
    });
    expect(http.agentsService.post).toHaveBeenCalled();
  });
});

describe("dataService", () => {
  it("gets the schema list", async () => {
    await dataService.getSchemaList({
      projectKey: "pk",
      pageNo: 1,
      pageSize: 20,
      sortDescending: true,
      sortBy: "CreatedDate",
      keyword: "",
      schemaType: "",
    });
    expect(http.dataService.get).toHaveBeenCalledWith(
      expect.stringContaining("ProjectKey=pk"),
      undefined,
      { absoluteUrl: true },
    );
  });

  it("gets schema details", async () => {
    await dataService.getSchemaDetails("id1", "pk");
    expect(http.dataService.get).toHaveBeenCalledWith(
      expect.stringContaining("id=id1"),
      undefined,
      { absoluteUrl: true },
    );
  });
});

describe("languageManagerService", () => {
  it("fetches languages for a project", async () => {
    await languageManagerService.fetchBlocksLanguages("pk");
    expect(http.logicService.get).toHaveBeenCalledWith(
      expect.stringContaining("projectKey=pk"),
      undefined,
      { absoluteUrl: true },
    );
  });
});

describe("authClientService", () => {
  it("gets client credentials for a project", async () => {
    await authClientService.clients.getClientCredentials({ projectKey: "pk" });
    expect(http.iamService.get).toHaveBeenCalledWith(
      expect.stringContaining("ProjectKey=pk"),
      undefined,
      { absoluteUrl: true },
    );
  });
});
