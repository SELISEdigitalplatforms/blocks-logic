import { languageManagerService } from "@blocks-workflow/services/language.manager.service";
import { NodeGuideActionSendMailV1 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
import { emailService } from "@blocks-workflow/services/email.services";
import { IEmailTemplate } from "../../models/email";
import { extractTemplateBodyKeys } from "../../utils/extract-template-keys";
import { tStatic } from "@seliseblocks/genesis-os";

const LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE = "logic.workflow.nodes.schemas";

// ── Shared template cache ──────────────────────────────────────────────
// Both the template select `options` and the body-map `fixedKeys` resolve
// from the same cached promise so only ONE API call is made per project.
let _cachedTenantId = "";
let _cachedPromise: Promise<IEmailTemplate[]> | null = null;

function getTemplates(tenantId: string): Promise<IEmailTemplate[]> {
  if (_cachedTenantId === tenantId && _cachedPromise) {
    return _cachedPromise;
  }
  _cachedTenantId = tenantId;
  _cachedPromise = emailService
    .fetchEmailTemplates(0, 100, "", "Name", false, "", "")
    .then((res) => res.templates);
  return _cachedPromise;
}

function findTemplate(
  templates: IEmailTemplate[],
  emailTemplate: string,
): IEmailTemplate | undefined {
  return templates.find((t) => {
    return (t.name || "") === emailTemplate.split("_")[0];
  });
}

// ── Schema ─────────────────────────────────────────────────────────────

export const NodeSchemaActionSendMailV1: NodeSchemaDefinition = {
  guide: NodeGuideActionSendMailV1,
  schema: {
    type: "sendMail",
    category: "action",
    version: "v1",
    parameters: [
      {
        id: "template",
        type: "select",
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_SEND_MAIL_V1.TEMPLATE"); },
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_SEND_MAIL_V1.CHOOSE_THE_EMAIL_TEMPLATE"); },
        key: "EmailTemplate",
        required: true,
        options: (_data, config) => {
          return getTemplates(config.tenantId).then((templates) => {
            if (!templates.length) return [];
            return templates.map((t) => ({
              label: t.name || "",
              value: `${t.name}_${config.tenantId}`,
            }));
          });
        },
        onChange(value) {
          const [Template] = (value as string).split("_");
          return {
            EmailTemplate: value,
            Template,
          };
        },
      },
      {
        id: "laguage",
        type: "select",
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_SEND_MAIL_V1.LANGUAGE"); },
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_SEND_MAIL_V1.CHOOSE_LANGUAGE_FOR_THE"); },
        key: "Language",
        required: true,
        options: (_data, _config) => {
          return new Promise((resolve, reject) => {
            languageManagerService
              .fetchBlocksLanguages()
              .then((res) => {
                if (!res.length) return resolve([]);
                resolve(
                  res.map((language) => ({
                    label: language.languageName,
                    value: language.languageCode,
                  })),
                );
              })
              .catch(reject);
          });
        },
      },
      {
        id: "to-email",
        type: "text",
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_SEND_MAIL_V1.TO_EMAIL"); },
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_SEND_MAIL_V1.EMAIL_ADDRESS_OF_THE"); },
        key: "To",
        required: true,
      },
      {
        id: "map-body-dynmaic",
        type: "fixed-key-value-pairs",
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_SEND_MAIL_V1.MAP_BODY"); },
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_SEND_MAIL_V1.MAP_DYNAMIC_VALUE"); },
        key: "BodyDataContext",
        get keyLabel() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_SEND_MAIL_V1.TEMPLATE_KEY"); },
        get valueLabel() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_SEND_MAIL_V1.MAPPED_VALUE"); },
        fixedKeysDependencies: ["EmailTemplate"],
        fixedKeys: (data, config) => {
          const emailTemplate = data.EmailTemplate;
          if (!emailTemplate || typeof emailTemplate !== "string") {
            return Promise.resolve([]);
          }
          return getTemplates(config.tenantId).then((templates) => {
            const selected = findTemplate(templates, emailTemplate);
          return extractTemplateBodyKeys(selected?.templateBody);
        });
      },
    },
      {
        id: "attachments",
        type: "expression-list",
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_SEND_MAIL_V1.ATTACHMENTS"); },
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_SEND_MAIL_V1.STORAGE_FILE_IDS_TO"); },
        key: "Attachments",
        required: false,
        placeholder: "File ID or {{ expression }}",
        get addButtonText() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_SEND_MAIL_V1.ADD_ATTACHMENT"); },
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: {
      EmailTemplate: "",
      Template: "",
      Language: "",
      To: "",
      BodyDataContext: {},
      Attachments: [],
    },
    settings: {},
  },
  transform: (node) => node,
};
