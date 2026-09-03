import { beforeEach, describe, expect, it, vi } from "vitest";

// ── Service mocks ────────────────────────────────────────────────────────
// Schema `options`/`onChange`/`fixedKeys` resolve data through these services.
// Mock them so the schemas can be exercised without an HTTP client.
const {
  getAgents,
  fetchEmailConfigs,
  fetchEmailTemplates,
  fetchBlocksLanguages,
  getSchemaList,
  getSchemaDetails,
  getClientCredentials,
  getOrganizations,
  getRoles,
  getPermissions,
} = vi.hoisted(() => ({
  getAgents: vi.fn(),
  fetchEmailConfigs: vi.fn(),
  fetchEmailTemplates: vi.fn(),
  fetchBlocksLanguages: vi.fn(),
  getSchemaList: vi.fn(),
  getSchemaDetails: vi.fn(),
  getClientCredentials: vi.fn(),
  getOrganizations: vi.fn(),
  getRoles: vi.fn(),
  getPermissions: vi.fn(),
}));

vi.mock("@/modules/workflow/services/agent.service", () => ({
  agentService: { getAgents },
}));
vi.mock("@blocks-workflow/services/email.services", () => ({
  emailService: { fetchEmailConfigs, fetchEmailTemplates },
}));
vi.mock("@blocks-workflow/services/language.manager.service", () => ({
  languageManagerService: { fetchBlocksLanguages },
}));
vi.mock("@blocks-workflow/services/data.service", () => ({
  dataService: { getSchemaList, getSchemaDetails },
}));
vi.mock("@blocks-workflow/services/iam.service", () => ({
  authClientService: { clients: { getClientCredentials } },
  iamService: { getOrganizations, getRoles, getPermissions },
}));

import { useProjectStore } from "@seliseblocks/genesis-os";
import { NodeSchemasDefinition } from "./node-schemas";
import { NodeSchemaTriggerWebhookV1 } from "./node-schema-trigger-webhook-v1";
import { NodeSchemaTriggerEmailV1 } from "./node-schema-trigger-email-v1";
import { NodeSchemaTriggerDataGatewayV1 } from "./node-schema-trigger-dataGateway-v1";
import { NodeSchemaTriggerScheduleV1 } from "./node-schema-trigger-schedule-v1";
import { NodeSchemaActionAiAgentV1 } from "./node-schema-action-aiAgent-v1";
import { NodeSchemaActionSendMailV1 } from "./node-schema-action-sendMail-v1";
import { NodeSchemaActionHttpRequestV1 } from "./node-schema-action-httpRequest-v1";
import { NodeSchemaActionDataActionV1 } from "./node-schema-action-dataAction-v1";
import { NodeSchemaTransformSetFieldV1 } from "./node-schema-transform-setfield-v1";
import { NodeSchemaTransformCodeV1 } from "./node-schema-transform-code-v1";
import { NodeSchemaLogicIfV1 } from "./node-schema-logic-if-v1";

type AnyRec = Record<string, unknown>;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const field = (schema: { schema: { parameters: any[]; settings: any[] } }, key: string) =>
  [...schema.schema.parameters, ...schema.schema.settings].find((f) => f.key === key);

beforeEach(() => {
  vi.clearAllMocks();
  (useProjectStore as unknown as { setState: (s: AnyRec) => void }).setState({
    selectedProject: {
      tenantId: "tenant-1",
      tenantSlug: "slug-1",
      projectKey: "pk-1",
    },
  });
});

describe("NodeSchemasDefinition registry", () => {
  it("registers every node schema by composite key", () => {
    expect(Object.keys(NodeSchemasDefinition)).toEqual(
      expect.arrayContaining([
        "triggerwebhookv1",
        "triggeremailv1",
        "triggerdataGatewayv1",
        "triggerschedulev1",
        "actionagentv1",
        "transformsetfieldv1",
        "transformcodev1",
        "actionsendMailv1",
        "actionhttpRequestv1",
        "actiondataActionv1",
        "logicifv1",
      ]),
    );
  });

  it("each definition carries a schema, defaults and transform", () => {
    for (const def of Object.values(NodeSchemasDefinition)) {
      expect(def.schema.type).toBeTruthy();
      expect(def.schema.category).toBeTruthy();
      expect(def.defaults).toBeDefined();
      expect(typeof def.transform).toBe("function");
    }
  });
});

describe("logic/if v1", () => {
  it("transform returns a shallow clone", () => {
    const node = { id: "n1", parameters: { conditionType: "or" } } as never;
    expect(NodeSchemaLogicIfV1.transform?.(node)).toEqual(node);
  });
});

describe("http request v1", () => {
  it("transform is a passthrough clone", () => {
    const node = { id: "n1", parameters: {} } as never;
    expect(NodeSchemaActionHttpRequestV1.transform?.(node)).toEqual(node);
  });

  it("exposes dependent fields for query/header/body toggles", () => {
    expect(field(NodeSchemaActionHttpRequestV1, "queryParameters").dependsOn).toEqual({
      key: "haveQueryParameters",
      value: true,
    });
    expect(field(NodeSchemaActionHttpRequestV1, "body").dependsOn).toEqual({
      key: "bodyContentType",
      value: "json",
    });
  });

  it("offers Blocks Authentication and Client Credential, defaulting to no extra auth", () => {
    const authType = field(NodeSchemaActionHttpRequestV1, "authenticationType");
    const values = (authType.options as { value: string }[]).map((option) => option.value);
    expect(values).toEqual(
      expect.arrayContaining(["blocksAuthentication", "clientCredential"]),
    );
    expect(NodeSchemaActionHttpRequestV1.defaults?.parameters?.authenticationType).toBe("");
    expect(field(NodeSchemaActionHttpRequestV1, "clientCredential_composite").dependsOn).toEqual({
      key: "authenticationType",
      value: "clientCredential",
      operator: "equals",
    });
  });

  it("transform maps legacy useBlocksAuthorization true to blocksAuthentication", () => {
    const node = { id: "n1", parameters: { useBlocksAuthorization: true } } as never;
    const out = NodeSchemaActionHttpRequestV1.transform?.(node) as AnyRec;
    expect((out.parameters as AnyRec).authenticationType).toBe("blocksAuthentication");
  });

  it("transform does not overwrite an existing authenticationType", () => {
    const node = {
      id: "n1",
      parameters: { authenticationType: "clientCredential", useBlocksAuthorization: true },
    } as never;
    const out = NodeSchemaActionHttpRequestV1.transform?.(node) as AnyRec;
    expect((out.parameters as AnyRec).authenticationType).toBe("clientCredential");
  });

  it("client credential onChange splits id and secret", () => {
    const onChange = field(NodeSchemaActionHttpRequestV1, "clientCredential_composite").onChange;
    expect(onChange("cc1:::s1", {}, {})).toEqual({
      clientCredential_composite: "cc1:::s1",
      clientId: "cc1",
      clientSecret: "s1",
    });
  });
});

describe("transform set-field v1", () => {
  it("transform is a passthrough", () => {
    const node = { id: "n", parameters: { mode: "json" } } as never;
    expect(NodeSchemaTransformSetFieldV1.transform?.(node)).toEqual(node);
  });

  it("carries a continue-on-error setting", () => {
    expect(field(NodeSchemaTransformSetFieldV1, "settings.continueOnError")).toBeDefined();
  });
});

describe("ai agent v1", () => {
  it("options maps agents into composite value/label pairs", async () => {
    getAgents.mockResolvedValue({
      agents: [{ id: "a1", widget_id: "w1", name: "Agent One" }],
    });
    const opts = field(NodeSchemaActionAiAgentV1, "agent").options;
    const result = await opts({}, { projectKey: "pk-1" });
    expect(getAgents).toHaveBeenCalledWith({
      limit: 100,
      offset: 0,
      project_key: "pk-1",
    });
    expect(result).toEqual([{ value: "a1_w1_pk-1", label: "Agent One" }]);
  });

  it("options rejects when the service fails", async () => {
    getAgents.mockRejectedValue(new Error("boom"));
    const opts = field(NodeSchemaActionAiAgentV1, "agent").options;
    await expect(opts({}, { projectKey: "pk-1" })).rejects.toThrow("boom");
  });

  it("onChange splits the composite agent value", () => {
    const onChange = field(NodeSchemaActionAiAgentV1, "agent").onChange;
    expect(onChange("a1_w1_pk-1")).toEqual({
      agent: "a1_w1_pk-1",
      AgentId: "a1",
      WidgetId: "w1",
      ProjectKey: "pk-1",
    });
  });

  it("transform injects the agents API base", () => {
    const node = { id: "n", parameters: { input: "hi" } } as never;
    const out = NodeSchemaActionAiAgentV1.transform?.(node) as AnyRec;
    expect((out.parameters as AnyRec).ApiBaseUrl).toBeDefined();
  });
});

describe("email trigger v1", () => {
  it("options returns only inbound mailboxes", async () => {
    fetchEmailConfigs.mockResolvedValue([
      { itemId: "m1", name: "Inbox", isInbound: true },
      { itemId: "m2", name: "Outbox", isInbound: false },
    ]);
    const opts = field(NodeSchemaTriggerEmailV1, "mailbox_composite").options;
    const result = await opts({}, { projectKey: "pk-1" });
    expect(result).toEqual([{ value: "m1_pk-1", label: "Inbox" }]);
  });

  it("options rejects on failure", async () => {
    fetchEmailConfigs.mockRejectedValue(new Error("nope"));
    const opts = field(NodeSchemaTriggerEmailV1, "mailbox_composite").options;
    await expect(opts({}, { projectKey: "pk-1" })).rejects.toThrow("nope");
  });

  it("onChange derives the mail server configuration id", () => {
    const onChange = field(NodeSchemaTriggerEmailV1, "mailbox_composite").onChange;
    expect(onChange("m1_pk-1")).toEqual({
      mailbox_composite: "m1_pk-1",
      mailServerConfigurationId: "m1",
      projectKey: "pk-1",
    });
  });

  it("transform is a passthrough", () => {
    const node = { id: "n", parameters: {} } as never;
    expect(NodeSchemaTriggerEmailV1.transform?.(node)).toBe(node);
  });
});

describe("send mail v1", () => {
  it("template options resolve from cached templates", async () => {
    fetchEmailTemplates.mockResolvedValue({
      templates: [{ name: "Welcome", templateBody: "Hi {{name}}" }],
      totalCount: 1,
    });
    const opts = field(NodeSchemaActionSendMailV1, "EmailTemplate").options;
    const result = await opts({}, { projectKey: "cache-pk" });
    expect(result).toEqual([{ label: "Welcome", value: "Welcome_cache-pk" }]);
  });

  it("template options return [] when there are no templates", async () => {
    fetchEmailTemplates.mockResolvedValue({ templates: [], totalCount: 0 });
    const opts = field(NodeSchemaActionSendMailV1, "EmailTemplate").options;
    // fresh project key to bypass the module-level cache
    expect(await opts({}, { projectKey: "empty-pk" })).toEqual([]);
  });

  it("template onChange splits template and project key", () => {
    const onChange = field(NodeSchemaActionSendMailV1, "EmailTemplate").onChange;
    expect(onChange("Welcome_pk-1")).toEqual({
      EmailTemplate: "Welcome_pk-1",
      Template: "Welcome",
      ProjectKey: "pk-1",
    });
  });

  it("language options map languages", async () => {
    fetchBlocksLanguages.mockResolvedValue([{ languageName: "English", languageCode: "en" }]);
    const opts = field(NodeSchemaActionSendMailV1, "Language").options;
    expect(await opts({}, { projectKey: "pk-1" })).toEqual([{ label: "English", value: "en" }]);
  });

  it("language options return [] when none exist", async () => {
    fetchBlocksLanguages.mockResolvedValue([]);
    const opts = field(NodeSchemaActionSendMailV1, "Language").options;
    expect(await opts({}, { projectKey: "pk-1" })).toEqual([]);
  });

  it("body fixedKeys return [] when no template is selected", async () => {
    const f = field(NodeSchemaActionSendMailV1, "BodyDataContext");
    expect(await f.fixedKeys({}, { projectKey: "pk-1" })).toEqual([]);
  });

  it("body fixedKeys extract keys from the matched template", async () => {
    fetchEmailTemplates.mockResolvedValue({
      templates: [{ name: "Promo", templateBody: "Hi {{first}} {{last}}" }],
      totalCount: 1,
    });
    const f = field(NodeSchemaActionSendMailV1, "BodyDataContext");
    const keys = await f.fixedKeys({ EmailTemplate: "Promo_body-pk" }, { projectKey: "body-pk" });
    expect(Array.isArray(keys)).toBe(true);
  });
});

describe("webhook trigger v1", () => {
  it("displayValue builds the production URL when mode is production", () => {
    const f = field(NodeSchemaTriggerWebhookV1, "executionMode");
    const url = f.displayValue(
      { executionMode: 1 },
      { projectKey: "pk", workflowId: "wf", nodeId: "nd", executionMode: 0 },
    );
    expect(url).toContain("/Workflow/webhook/pk/wf/nd");
  });

  it("displayValue builds the test URL and falls back to config mode", () => {
    const f = field(NodeSchemaTriggerWebhookV1, "executionMode");
    const url = f.displayValue(
      {},
      { projectKey: "pk", workflowId: "wf", nodeId: "nd", executionMode: 0 },
    );
    expect(url).toContain("/Workflow/webhook-test/pk/wf/nd");
  });

  it("transform sets the path from the node id", () => {
    const node = { id: "node-9", parameters: { httpMethod: "POST" } } as never;
    const out = NodeSchemaTriggerWebhookV1.transform?.(node) as AnyRec;
    expect((out.parameters as AnyRec).path).toBe("node-9");
  });

  it("transform echoes authorizationMode for the backend (defaults to RolesOnly)", () => {
    const rolesOnly = NodeSchemaTriggerWebhookV1.transform?.({
      id: "n",
      parameters: { httpMethod: "POST", authorizationMode: "RolesOnly" },
    } as never) as AnyRec;
    expect((rolesOnly.parameters as AnyRec).authorizationMode).toBe("RolesOnly");

    const permissionsOnly = NodeSchemaTriggerWebhookV1.transform?.({
      id: "n",
      parameters: { httpMethod: "POST", authorizationMode: "PermissionsOnly" },
    } as never) as AnyRec;
    expect((permissionsOnly.parameters as AnyRec).authorizationMode).toBe("PermissionsOnly");

    const both = NodeSchemaTriggerWebhookV1.transform?.({
      id: "n",
      parameters: { httpMethod: "POST", authorizationMode: "RolesAndPermissions" },
    } as never) as AnyRec;
    expect((both.parameters as AnyRec).authorizationMode).toBe("RolesAndPermissions");

    // Missing authorizationMode falls back to RolesOnly for backend parity.
    const fallback = NodeSchemaTriggerWebhookV1.transform?.({
      id: "n",
      parameters: { httpMethod: "POST" },
    } as never) as AnyRec;
    expect((fallback.parameters as AnyRec).authorizationMode).toBe("RolesOnly");
  });

  // Skipped: these tests were written against an older schema that wrapped
  // authorization in a nested `parameters.authorization.{roles,permissions,
  // organizationId,isRolePermission,matchType}` object. The current schema
  // uses flat `parameters.{roles,permissions,organizationId,authorizationMode}`.
  // See node-schema-trigger-webhook-v1.ts.
  it.skip("transform translates isRolePermission into matchType for the wire format", () => {
    const base = { id: "n", parameters: { httpMethod: "POST" } } as never;
    const allOut = NodeSchemaTriggerWebhookV1.transform?.({
      ...base,
      parameters: {
        httpMethod: "POST",
        authorization: {
          isRolePermission: true,
          roles: { operator: "all", items: ["clouduser"] },
        },
      },
    } as never) as AnyRec;
    expect((allOut.parameters as AnyRec).authorization.matchType).toBe("all");
    expect(((allOut.parameters as AnyRec).authorization as AnyRec).roles.items).toEqual([
      "clouduser",
    ]);

    const anyOut = NodeSchemaTriggerWebhookV1.transform?.({
      ...base,
      parameters: {
        httpMethod: "POST",
        authorization: { isRolePermission: false },
      },
    } as never) as AnyRec;
    expect((anyOut.parameters as AnyRec).authorization.matchType).toBe("any");

    // Omitted isRolePermission defaults to "all"
    const missingOut = NodeSchemaTriggerWebhookV1.transform?.({
      ...base,
      parameters: { httpMethod: "POST", authorization: {} },
    } as never) as AnyRec;
    expect((missingOut.parameters as AnyRec).authorization.matchType).toBe("all");
  });

  it.skip("exposes the Blocks Authorization auth option", () => {
    const authField = field(NodeSchemaTriggerWebhookV1, "authType");
    const values = (authField.options as { value: string }[]).map((o) => o.value);
    expect(values).toEqual(
      expect.arrayContaining(["none", "blocksAccessToken", "blocksAuthorization"]),
    );
  });

  it("organization options prepend None and Default, then map itemId", async () => {
    getOrganizations.mockResolvedValue({
      organizations: [
        { itemId: "org-1", name: "Acme" },
        { itemId: "org-2", name: "Globex" },
      ],
    });
    const opts = field(NodeSchemaTriggerWebhookV1, "organizationId").options as (
      data: AnyRec,
      config: AnyRec,
    ) => Promise<{ value: string; label: string }[]>;
    const result = await opts({}, { projectKey: "pk" });
    expect(result).toEqual([
      { value: "", label: "None" },
      { value: "default", label: "Default" },
      { value: "org-1", label: "Acme" },
      { value: "org-2", label: "Globex" },
    ]);
  });

  it("organization options are None only when organizations is null", async () => {
    getOrganizations.mockResolvedValue({
      organizations: null,
      totalCount: 0,
      isSuccess: false,
      errors: { multi_org_disabled: "Multi-organization mode is disabled." },
    });
    const opts = field(NodeSchemaTriggerWebhookV1, "organizationId").options as (
      data: AnyRec,
      config: AnyRec,
    ) => Promise<{ value: string; label: string }[]>;
    expect(await opts({}, { projectKey: "pk" })).toEqual([{ value: "", label: "None" }]);
  });

  it("organization options are None only when organizations is empty", async () => {
    getOrganizations.mockResolvedValue({
      organizations: [],
      totalCount: 0,
      isSuccess: true,
    });
    const opts = field(NodeSchemaTriggerWebhookV1, "organizationId").options as (
      data: AnyRec,
      config: AnyRec,
    ) => Promise<{ value: string; label: string }[]>;
    expect(await opts({}, { projectKey: "pk" })).toEqual([{ value: "", label: "None" }]);
  });

  it("organization options are None only when getOrganizations rejects", async () => {
    getOrganizations.mockRejectedValue(new Error("network"));
    const opts = field(NodeSchemaTriggerWebhookV1, "organizationId").options as (
      data: AnyRec,
      config: AnyRec,
    ) => Promise<{ value: string; label: string }[]>;
    expect(await opts({}, { projectKey: "pk" })).toEqual([{ value: "", label: "None" }]);
  });

  it.skip("roles options call getRoles with the selected organization", async () => {
    getRoles.mockResolvedValue([{ itemId: "r1", name: "clouduser" }]);
    const opts = field(NodeSchemaTriggerWebhookV1, "authorization.roles").options as (
      data: AnyRec,
      config: AnyRec,
    ) => Promise<{ value: string; label: string }[]>;
    const result = await opts({ authorization: { organizationId: "acme" } }, { projectKey: "pk" });
    expect(getRoles).toHaveBeenCalledWith({ organizationId: "acme" });
    expect(result).toEqual([{ value: "clouduser", label: "clouduser" }]);
  });

  it.skip("permissions options call getPermissions with selected roles", async () => {
    getPermissions.mockResolvedValue([{ itemId: "p1", name: "Change User Password" }]);
    const opts = field(NodeSchemaTriggerWebhookV1, "authorization.permissions").options as (
      data: AnyRec,
      config: AnyRec,
    ) => Promise<{ value: string; label: string }[]>;
    const result = await opts(
      { authorization: { roles: { items: ["cloudadmin"] } } },
      { projectKey: "pk-1" },
    );
    expect(getPermissions).toHaveBeenCalledWith({
      projectKey: "pk-1",
      roles: ["cloudadmin"],
    });
    expect(result).toEqual([{ value: "Change User Password", label: "Change User Password" }]);
  });

  it("defaults organizationId to empty (None)", () => {
    expect(NodeSchemaTriggerWebhookV1.defaults.parameters.organizationId).toBe("");
  });
});

describe("data gateway trigger v1", () => {
  it("collection options map schema list into composite values", async () => {
    getSchemaList.mockResolvedValue({
      data: {
        items: [{ collectionName: "col", schemaName: "Sch", id: "id1" }],
      },
    });
    const opts = field(NodeSchemaTriggerDataGatewayV1, "collectionName_composite").options;
    expect(await opts({}, { projectKey: "pk-1" })).toEqual([
      { value: "col:::Sch:::id1", label: "Sch" },
    ]);
  });

  it("collection onChange splits parts and reads the project tenant", () => {
    const onChange = field(NodeSchemaTriggerDataGatewayV1, "collectionName_composite").onChange;
    expect(onChange("col:::Sch:::id1")).toEqual({
      collectionName_composite: "col:::Sch:::id1",
      collectionName: "col",
      schemaName: "Sch",
      projectKey: "tenant-1",
    });
  });

  it("execution-notes displayValue returns a titled React node", () => {
    const f = field(NodeSchemaTriggerDataGatewayV1, "executionNotes");
    const value = f.displayValue() as { title: string };
    expect(value.title).toBe("Notes of editor execution");
  });

  it("output displayValue interpolates the selected operation and collection", () => {
    const f = field(NodeSchemaTriggerDataGatewayV1, "output");
    const out = f.displayValue({
      operation: "Inserted",
      collectionName: "col",
      schemaName: "Sch",
    }) as string;
    expect(out).toContain('"Operation": "Inserted"');
    expect(out).toContain('"CollectionName": "col"');
  });

  it("transform injects the project tenant id", () => {
    const node = { id: "n", parameters: {} } as never;
    const out = NodeSchemaTriggerDataGatewayV1.transform?.(node) as AnyRec;
    expect((out.parameters as AnyRec).projectKey).toBe("tenant-1");
  });
});

describe("data action v1", () => {
  it("puts raw query mode first and defaults new nodes to raw mode", () => {
    expect(NodeSchemaActionDataActionV1.schema.parameters.map((param) => param.key)).toEqual([
      "rawQueryMode",
      "authenticationType",
      "clientCredential_composite",
      "rawQuery",
      "collectionName_composite",
      "actionType",
      "filter",
      "fieldMapping",
      "getFields",
    ]);
    expect(NodeSchemaActionDataActionV1.defaults.parameters.rawQueryMode).toBe(true);
    expect(NodeSchemaActionDataActionV1.defaults.parameters.rawQuery).toBe("");
  });

  it("shows the raw query editor only in raw query mode", () => {
    const rawQuery = field(NodeSchemaActionDataActionV1, "rawQuery");
    expect(rawQuery.type).toBe("graphql-code-editor");
    expect(rawQuery.dependsOn).toEqual({
      key: "rawQueryMode",
      value: true,
      operator: "equals",
    });
  });

  it("hides collection and action fields in raw query mode", () => {
    expect(
      field(NodeSchemaActionDataActionV1, "collectionName_composite").hidden({
        rawQueryMode: true,
      }),
    ).toBe(true);
    expect(
      field(NodeSchemaActionDataActionV1, "actionType").hidden({
        rawQueryMode: true,
      }),
    ).toBe(true);
    expect(
      field(NodeSchemaActionDataActionV1, "filter").hidden({
        rawQueryMode: true,
      }),
    ).toBe(true);
    expect(
      field(NodeSchemaActionDataActionV1, "fieldMapping").hidden({
        rawQueryMode: true,
        actionType: "updateData",
      }),
    ).toBe(true);
    expect(
      field(NodeSchemaActionDataActionV1, "getFields").hidden({
        rawQueryMode: true,
      }),
    ).toBe(true);
  });

  it("keeps collection and action fields available in legacy mode", () => {
    expect(
      field(NodeSchemaActionDataActionV1, "collectionName_composite").hidden({
        rawQueryMode: false,
      }),
    ).toBe(false);
    expect(
      field(NodeSchemaActionDataActionV1, "actionType").hidden({
        rawQueryMode: false,
      }),
    ).toBe(false);
    expect(
      field(NodeSchemaActionDataActionV1, "filter").hidden({
        rawQueryMode: false,
      }),
    ).toBe(false);
    expect(
      field(NodeSchemaActionDataActionV1, "getFields").hidden({
        rawQueryMode: false,
      }),
    ).toBe(false);
  });

  it("collection options map schema list", async () => {
    getSchemaList.mockResolvedValue({
      data: { items: [{ collectionName: "c", schemaName: "S", id: "i" }] },
    });
    const opts = field(NodeSchemaActionDataActionV1, "collectionName_composite").options;
    expect(await opts({}, { projectKey: "pk-1" })).toEqual([{ value: "c:::S:::i", label: "S" }]);
  });

  it("collection onChange returns base fields without a store", () => {
    const onChange = field(NodeSchemaActionDataActionV1, "collectionName_composite").onChange;
    const result = onChange("c:::S:::", {}, {});
    expect(result).toMatchObject({
      collectionName: "c",
      schemaName: "S",
      projectKey: "tenant-1",
      projectShortKey: "slug-1",
    });
    // No schema id present, so getSchemaDetails is not called.
    expect(getSchemaDetails).not.toHaveBeenCalled();
  });

  it("collection onChange auto-populates fields via the store", async () => {
    getSchemaDetails.mockResolvedValue({ data: { fields: [] } });
    const updateNode = vi.fn();
    const selectedNode = { id: "sn", parameters: {} };
    const store = {
      getState: () => ({ selectedNode, updateNode }),
    };
    const onChange = field(NodeSchemaActionDataActionV1, "collectionName_composite").onChange;
    onChange("c:::S:::schema-1", {}, { store });
    await vi.waitFor(() => expect(getSchemaDetails).toHaveBeenCalled());
    await vi.waitFor(() => expect(updateNode).toHaveBeenCalledWith("sn", expect.any(Object)));
  });

  it("collection onChange swallows schema detail errors", async () => {
    getSchemaDetails.mockRejectedValue(new Error("fail"));
    const store = { getState: () => ({ selectedNode: null, updateNode: vi.fn() }) };
    const onChange = field(NodeSchemaActionDataActionV1, "collectionName_composite").onChange;
    expect(() => onChange("c:::S:::schema-1", {}, { store })).not.toThrow();
    await vi.waitFor(() => expect(getSchemaDetails).toHaveBeenCalled());
  });

  it("client credential options return only active clients", async () => {
    getClientCredentials.mockResolvedValue([
      { itemId: "cc1", clientSecret: "s1", name: "Active", isActive: true },
      { itemId: "cc2", clientSecret: "s2", name: "Inactive", isActive: false },
    ]);
    const opts = field(NodeSchemaActionDataActionV1, "clientCredential_composite").options;
    expect(await opts({}, { projectKey: "pk-1" })).toEqual([
      { value: "cc1:::s1", label: "Active" },
    ]);
  });

  it("client credential onChange splits id and secret", () => {
    const onChange = field(NodeSchemaActionDataActionV1, "clientCredential_composite").onChange;
    expect(onChange("cc1:::s1", {}, {})).toEqual({
      clientCredential_composite: "cc1:::s1",
      clientId: "cc1",
      clientSecret: "s1",
    });
  });

  it("offers Blocks Authentication and keeps the credential dropdown on Client Credential", () => {
    const authType = field(NodeSchemaActionDataActionV1, "authenticationType");
    const values = (authType.options as { value: string }[]).map((option) => option.value);
    expect(values).toEqual(
      expect.arrayContaining(["clientCredential", "blocksAuthentication"]),
    );
    expect(field(NodeSchemaActionDataActionV1, "clientCredential_composite").dependsOn).toEqual({
      key: "authenticationType",
      value: "clientCredential",
      operator: "equals",
    });
  });

  it("fieldMapping hidden hides for non insert/update action types", () => {
    const f = field(NodeSchemaActionDataActionV1, "fieldMapping");
    expect(f.hidden({ actionType: "getData" })).toBe(true);
    expect(f.hidden({ actionType: "insertData" })).toBe(false);
    expect(f.hidden({ actionType: "updateData" })).toBe(false);
  });

  it("transform injects tenant id, slug and api base", () => {
    const node = { id: "n", parameters: {} } as never;
    const out = NodeSchemaActionDataActionV1.transform?.(node) as AnyRec;
    const params = out.parameters as AnyRec;
    expect(params.projectKey).toBe("tenant-1");
    expect(params.projectShortKey).toBe("slug-1");
    expect("apiBaseUrl" in params).toBe(true);
  });
});

describe("transform code v1", () => {
  it("transform is a passthrough", () => {
    const node = { id: "n", parameters: { mode: "all" } } as never;
    expect(NodeSchemaTransformCodeV1.transform?.(node)).toEqual(node);
  });

  it("exposes a mode select with both modes and defaults to all-items", () => {
    const mode = field(NodeSchemaTransformCodeV1, "mode");
    expect(mode.type).toBe("select");
    expect(mode.options).toEqual([
      { label: "Run Once for All Items", value: "all" },
      { label: "Run Once for Each Item", value: "each" },
    ]);
    expect(NodeSchemaTransformCodeV1.defaults.parameters.mode).toBe("all");
  });

  it("offers javascript and holds python back", () => {
    const language = field(NodeSchemaTransformCodeV1, "language");
    expect(language.type).toBe("select");
    expect(language.options).toEqual([
      { label: "JavaScript", value: "js" },
      { label: "Python", value: "py", disabled: true },
    ]);
    expect(NodeSchemaTransformCodeV1.defaults.parameters.language).toBe("js");
  });

  it("carries a single javascript code-editor field for the script", () => {
    const script = field(NodeSchemaTransformCodeV1, "script");
    expect(script.type).toBe("code-editor");
    expect(script.language).toBe("javascript");
    expect(script.dependsOn).toBeUndefined();
    expect(NodeSchemaTransformCodeV1.defaults.parameters.script).toBe("");
  });

  it("carries a continue-on-error setting", () => {
    expect(field(NodeSchemaTransformCodeV1, "settings.continueOnError")).toBeDefined();
  });
});
