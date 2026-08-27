import { NodeGuideTriggerEmailV1 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
import { emailService } from "@blocks-workflow/services/email.services";

export const NodeSchemaTriggerEmailV1: NodeSchemaDefinition = {
  guide: NodeGuideTriggerEmailV1,
  schema: {
    type: "email",
    category: "trigger",
    version: "v1",
    parameters: [
      {
        id: "mailbox",
        type: "select",
        label: "Mailbox",
        info: "Select the mailbox to monitor for incoming emails",
        key: "mailbox_composite",
        required: true,
        options: (_data, config) => {
          return new Promise((resolve, reject) => {
            emailService
              .fetchEmailConfigs(config.projectKey, 0, 200)
              .then((res) =>
                resolve(
                  res
                    .filter((item) => item.isInbound)
                    .map((item) => ({
                      value: `${item.itemId}_${config.projectKey}`,
                      label: item.name,
                    })),
                ),
              )
              .catch(reject);
          });
        },
        onChange: (value: unknown) => {
          const [mailbox, projectKey] = (value as string).split("_");
          return {
            mailbox_composite: value,
            mailServerConfigurationId: mailbox,
            projectKey,
          };
        },
      },
      //       {
      //         id: "output",
      //         type: "display",
      //         label: "Output",
      //         info: "Structure of the output data",
      //         key: "output",
      //         content: `\`\`\`json
      // {
      //   "ItemId": String,
      //   "MessageId": String,
      //   "MailServerConfigurationId": String,
      //   "Subject": String,
      //   "From": String,
      //   "To": String,
      //   "Body": String,
      //   "Status": String,
      //   "Error": String,
      //   "Date": String,
      //   "RawMime": String,
      //   "IsInbound": Boolean
      // }
      // \`\`\``,
      //       },
    ],
    settings: [],
  },
  defaults: {
    parameters: {
      mailbox_composite: "",
      mailServerConfigurationId: "",
      projectKey: "",
    },
    settings: {},
  },
  transform: (node) => node,
};
