import { registerTranslations } from "@seliseblocks/genesis-os";
import shared from "./logic.shared.en.json";
import eventTriggers from "./logic.event-triggers.en.json";
import functions from "./logic.functions.en.json";
import proxy from "./logic.proxy.en.json";
import scheduler from "./logic.scheduler.en.json";
import workflowNodes from "./logic.workflow.nodes.en.json";
import workflow from "./logic.workflow.en.json";

registerTranslations("logic.shared", shared);
registerTranslations("logic.event-triggers", eventTriggers);
registerTranslations("logic.functions", functions);
registerTranslations("logic.proxy", proxy);
registerTranslations("logic.scheduler", scheduler);
registerTranslations("logic.workflow", workflow);
registerTranslations("logic.workflow.nodes", workflowNodes);
