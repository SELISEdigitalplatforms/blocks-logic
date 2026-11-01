import React from "react";
import { NodeGuideTriggerEmailV1 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
import { emailService } from "@blocks-workflow/services/email.services";
import { tStatic } from "@seliseblocks/genesis-os";
import { renderRichText } from "@/lib/rich-text";

const LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE = "logic.workflow.nodes.schemas";

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
          title: tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_TRIGGER_EMAIL_V1.NOTES_OF_EDITOR_EXECUTION"),
          description: React.createElement("span", null, ...renderRichText(tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_TRIGGER_EMAIL_V1.IF_AN_INBOUND_EMAIL"), [(text: string) => React.createElement("code", { className: "bg-muted px-1.5 py-0.5 rounded-md text-sm font-mono text-primary font-semibold" }, text), (text: string) => React.createElement("code", { className: "bg-muted px-1.5 py-0.5 rounded-md text-sm font-mono text-primary font-semibold" }, text)])),
        }),
      },
      {
        id: "mailbox",
        type: "select",
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_TRIGGER_EMAIL_V1.MAILBOX"); },
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_TRIGGER_EMAIL_V1.SELECT_THE_MAILBOX_TO"); },
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
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_TRIGGER_EMAIL_V1.TEST_SUBJECT"); },
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_TRIGGER_EMAIL_V1.IF_AN_INBOUND_EMAIL_2"); },
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
