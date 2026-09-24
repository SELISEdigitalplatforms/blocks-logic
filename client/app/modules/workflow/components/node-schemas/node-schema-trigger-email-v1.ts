import React from "react";
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
        id: "execution-notes",
        type: "callout-accordion-display",
        key: "executionNotes",
        displayValue: () => ({
          title: "Notes of editor execution",
          description: React.createElement(
            "span",
            null,
            "If an inbound email's ",
            React.createElement(
              "code",
              {
                className:
                  "bg-muted px-1.5 py-0.5 rounded-md text-sm font-mono text-primary font-semibold",
              },
              "Subject",
            ),
            " exactly matches ",
            React.createElement(
              "code",
              {
                className:
                  "bg-muted px-1.5 py-0.5 rounded-md text-sm font-mono text-primary font-semibold",
              },
              "Test Subject",
            ),
            " (case-insensitive), this execution runs in Test mode against the current draft workflow. Other inbound mail uses the published version.",
          ),
        }),
      },
      {
        id: "mailbox",
        type: "select",
        label: "Mailbox",
        info: "Select the mailbox to monitor for incoming emails",
        key: "mailbox_composite",
        required: true,
        options: (_data, config) => {
          return emailService
            .fetchEmailConfigs(0, 200)
            .then((res) =>
              res
                .filter((item) => Boolean(item.isInbound) && Boolean(item.itemId))
                .map((item) => ({
                  value: `${item.itemId}_${config.tenantId}`,
                  label: item.name || item.itemId,
                })),
            );
        },
        onChange: (value: unknown) => {
          const composite = String(value ?? "");
          const separator = composite.indexOf("_");
          const mailbox =
            separator === -1 ? composite : composite.slice(0, separator);
          return {
            mailbox_composite: value,
            mailServerConfigurationId: mailbox,
          };
        },
      },
      {
        id: "test-subject",
        type: "text",
        label: "Test Subject",
        info: "If an inbound email's Subject exactly matches this value (case-insensitive), this execution runs in Test mode against the current draft workflow instead of the published version.",
        key: "testSubject",
        required: false,
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: {
      mailbox_composite: "",
      mailServerConfigurationId: "",
      testSubject: "",
    },
    settings: {},
  },
  transform: (node) => node,
};
