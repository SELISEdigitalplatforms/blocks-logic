import { languageManagerService } from "@blocks-workflow/services/language.manager.service";
import { NodeGuideActionSendMailV1 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
import { emailService } from "@blocks-workflow/services/email.services";
import { IEmailTemplate } from "../../models/email";
import { extractTemplateBodyKeys } from "../../utils/extract-template-keys";

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
        label: "Template",
        info: "Choose the email template to use",
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
        label: "Language",
        info: "Choose language for the email template",
        key: "Language",
        required: true,
        options: (_data, config) => {
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
        label: "To Email",
        info: "Email address of the recipient.",
        key: "To",
        required: true,
      },
      {
        id: "map-body-dynmaic",
        type: "fixed-key-value-pairs",
        label: "Map (Body)",
        info: "Map dynamic value",
        key: "BodyDataContext",
        keyLabel: "Template key",
        valueLabel: "Mapped value",
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
        label: "Attachments",
        info: 'Storage File IDs to attach. Use a literal File ID, or an expression such as {{$json.output.fileId}} or {{$node["NodeName"].json.output.fileId}} to resolve it per run.',
        key: "Attachments",
        required: false,
        placeholder: "File ID or {{ expression }}",
        addButtonText: "Add attachment",
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
