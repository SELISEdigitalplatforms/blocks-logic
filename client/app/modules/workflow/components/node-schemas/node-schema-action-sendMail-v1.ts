import { languageManagerService } from "@blocks-workflow/services/language.manager.service";
import { NodeSchemaDefinition } from "./node-schema.type";
import { emailService } from "@blocks-workflow/services/email.services";
import { IEmailTemplate } from "../../models/email";
import { extractTemplateBodyKeys } from "../../utils/extract-template-keys";

// ── Shared template cache ──────────────────────────────────────────────
// Both the template select `options` and the body-map `fixedKeys` resolve
// from the same cached promise so only ONE API call is made per project.
let _cachedProjectKey = "";
let _cachedPromise: Promise<IEmailTemplate[]> | null = null;

function getTemplates(projectKey: string): Promise<IEmailTemplate[]> {
  if (_cachedProjectKey === projectKey && _cachedPromise) {
    return _cachedPromise;
  }
  _cachedProjectKey = projectKey;
  _cachedPromise = emailService
    .fetchEmailTemplates(0, 100, projectKey, "", "Name", false, "", "")
    .then((res) => res.templates);
  return _cachedPromise;
}

function findTemplate(
  templates: IEmailTemplate[],
  emailTemplate: string,
  projectKey: string,
): IEmailTemplate | undefined {
  return templates.find((t) => {
    const compositeValue = `${t.name || ""}_${projectKey}`;
    return compositeValue === emailTemplate;
  });
}

// ── Schema ─────────────────────────────────────────────────────────────

export const NodeSchemaActionSendMailV1: NodeSchemaDefinition = {
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
          return getTemplates(config.projectKey).then((templates) => {
            if (!templates.length) return [];
            return templates.map((t) => ({
              label: t.name || "",
              value: `${t.name}_${config.projectKey}`,
            }));
          });
        },
        onChange(value) {
          const [Template, ProjectKey] = (value as string).split("_");
          return {
            EmailTemplate: value,
            Template,
            ProjectKey,
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
              .fetchBlocksLanguages(config.projectKey)
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

          // Resolves from the shared cache — no extra API call.
          // On initial load the select `options` call will have already
          // started (or completed) the same promise, so this is either
          // instant or waits for the single in-flight request.
          return getTemplates(config.projectKey).then((templates) => {
            const selected = findTemplate(templates, emailTemplate, config.projectKey);
            return extractTemplateBodyKeys(selected?.templateBody);
          });
        },
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: {
      EmailTemplate: "",
      Template: "",
      ProjectKey: "",
      Language: "",
      To: "",
      BodyDataContext: {},
    },
    settings: {},
  },
  transform: (node) => node,
};
