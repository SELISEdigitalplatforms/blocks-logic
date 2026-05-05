import { languageManagerService } from "@blocks-localization/services/language.manager.service";
import { NodeSchemaDefinition } from "./node-schema.type";
import { emailService } from "@blocks-communication/mail/services/email.services";

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
          return new Promise((resolve, reject) => {
            emailService
              .fetchEmailTemplates(
                0,
                100,
                config.projectKey,
                "",
                "Name",
                false,
                "",
                "",
              )
              .then((res) => {
                if (!res.templates.length) return resolve([]);
                resolve(
                  res.templates.map((templa) => ({
                    label: templa.name || "",
                    value: `${templa.name}_${config.projectKey}`,
                  })),
                );
              })
              .catch(reject);
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
        type: "key-value-pairs",
        label: "Map (Body)",
        info: "Map dynamic value",
        key: "BodyDataContext",
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
      BodyDataContext: [],
    },
    settings: {},
  },
  transform: (node) => node,
};
