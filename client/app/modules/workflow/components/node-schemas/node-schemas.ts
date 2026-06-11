import { NodeSchemaTriggerWebhookV1 } from "./node-schema-trigger-webhook-v1";
import { NodeSchemaTransformSetFieldV1 } from "./node-schema-transform-setfield-v1";
import { NodeSchemaDefinition } from "./node-schema.type";
import { NodeSchemaActionAiAgentV1 } from "./node-schema-action-aiAgent-v1";
import { NodeSchemaActionSendMailV1 } from "./node-schema-action-sendMail-v1";
import { NodeSchemaActionHttpRequestV1 } from "./node-schema-action-httpRequest-v1";
import { NodeSchemaTriggerEmailV1 } from "./node-schema-trigger-email-v1";
import { NodeSchemaTriggerDataGatewayV1 } from "./node-schema-trigger-dataGateway-v1";
import { NodeSchemaTriggerBlockscheduleV1 } from "./node-schema-trigger-blockschedule-v1";
import { NodeSchemaActionDataActionV1 } from "./node-schema-action-dataAction-v1";
import { NodeSchemaLogicIfV1 } from "./node-schema-logic-if-v1";

export const NodeSchemasDefinition: Record<string, NodeSchemaDefinition> = {
  triggerwebhookv1: NodeSchemaTriggerWebhookV1,
  triggeremailv1: NodeSchemaTriggerEmailV1,
  triggerdataGatewayv1: NodeSchemaTriggerDataGatewayV1,
  triggerblockschedulev1: NodeSchemaTriggerBlockscheduleV1,
  actionagentv1: NodeSchemaActionAiAgentV1,
  transformsetfieldv1: NodeSchemaTransformSetFieldV1,
  actionsendMailv1: NodeSchemaActionSendMailV1,
  actionhttpRequestv1: NodeSchemaActionHttpRequestV1,
  actiondataActionv1: NodeSchemaActionDataActionV1,
  logicifv1: NodeSchemaLogicIfV1,
};
