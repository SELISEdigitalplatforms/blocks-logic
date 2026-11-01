import { WorkflowNodeDefinition } from "@blocks-workflow/models/node.model";
import {
	GitFork,
	Webhook,
	SquarePen,
	Bot,
	Mail,
	Globe,
	Inbox,
	Database,
	Clock,
	DatabaseZap,
	Code2,
	FunctionSquare,
	Waypoints,
	Split,
	Signpost,
	Merge,
	Filter,
	BetweenHorizontalStart,
	Zap,
} from "lucide-react";
import { deriveMergeHandle } from "@blocks-workflow/utils/merge-handles";
import { deriveSwitchHandle, switchHandleLabel } from "@blocks-workflow/utils/switch-handles";
import { tStatic } from "@seliseblocks/genesis-os";

const LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE = "logic.workflow.nodes.inspector";

export const NodeDefinitions: WorkflowNodeDefinition[] = [
	{
		id: "trigger-webhook-v2",
		icon: <Webhook className="h-5 w-5 text-error" />,
		get title() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.WEBHOOK"); },
		get description() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.STARTS_THE_WORKFLOW_WHEN"); },
		type: "webhook",
		category: "trigger",
		version: "v2",
		defaultName: "Webhook",
		handle: {
			sources: [{ key: "source", label: "" }],
			targets: [],
		},
	},
	{
		id: "trigger-email-v2",
		icon: <Inbox className="h-5 w-5 text-success" />,
		get title() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.EMAIL_TRIGGER"); },
		get description() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.TRIGGERS_THE_WORKFLOW_WHEN"); },
		type: "email",
		category: "trigger",
		version: "v2",
		get defaultName() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.EMAIL_TRIGGER"); },
		handle: {
			sources: [{ key: "source", label: "" }],
			targets: [],
		},
	},
	{
		id: "trigger-dataGateway-v2",
		icon: <DatabaseZap className="h-5 w-5 text-red-500" />,
		get title() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.DATA_TRIGGER"); },
		get description() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.TRIGGERS_THE_WORKFLOW_WHEN_2"); },
		type: "dataGateway",
		category: "trigger",
		version: "v2",
		get defaultName() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.DATA_TRIGGER"); },
		handle: {
			sources: [{ key: "source", label: "" }],
			targets: [],
		},
	},
	{
		id: "trigger-blocksEvent-v2",
		icon: <Zap className="h-5 w-5 text-amber-500" />,
		get title() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.BLOCKS_EVENT"); },
		get description() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.STARTS_THE_WORKFLOW_WHEN_2"); },
		type: "blocksEvent",
		category: "trigger",
		version: "v2",
		get defaultName() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.BLOCKS_EVENT"); },
		handle: {
			sources: [{ key: "source", label: "" }],
			targets: [],
		},
	},
	{
		id: "trigger-schedule-v2",
		icon: <Clock className="h-5 w-5 text-rose-500" />,
		get title() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.SCHEDULE"); },
		get description() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.TRIGGERS_THE_WORKFLOW_ON"); },
		type: "schedule",
		category: "trigger",
		version: "v2",
		defaultName: "Schedule",
		handle: {
			sources: [{ key: "source", label: "" }],
			targets: [],
		},
	},
	{
		id: "action-agent-v2",
		icon: <Bot className="h-5 w-5 text-orange-500" />,
		get title() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.AGENT"); },
		get description() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.EXECUTES_AN_AI_POWERED"); },
		category: "action",
		type: "agent",
		version: "v2",
		defaultName: "Agent",
		handle: {
			sources: [{ key: "source", label: "" }],
			targets: [{ key: "target", label: "" }],
		},
	},
	{
		id: "action-sendMail-v2",
		icon: <Mail className="h-5 w-5 text-purple-500" />,
		get title() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.SEND_MAIL"); },
		get description() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.SENDS_AN_EMAIL_AS"); },
		category: "action",
		type: "sendMail",
		version: "v2",
		get defaultName() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.SEND_MAIL"); },
		handle: {
			sources: [{ key: "source", label: "" }],
			targets: [{ key: "target", label: "" }],
		},
	},
	{
		id: "action-httpRequest-v2",
		icon: <Globe className="h-5 w-5 text-indigo-500" />,
		get title() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.HTTP_REQUEST"); },
		get description() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.SENDS_AN_HTTP_REQUEST"); },
		category: "action",
		type: "httpRequest",
		version: "v2",
		get defaultName() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.HTTP_REQUEST"); },
		handle: {
			sources: [{ key: "source", label: "" }],
			targets: [{ key: "target", label: "" }],
		},
	},
	{
		id: "action-proxy-v2",
		icon: <Waypoints className="h-5 w-5 text-teal-500" />,
		get title() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.PROXY"); },
		get description() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.CALL_A_CONFIGURED_PROXY"); },
		type: "proxy",
		category: "action",
		version: "v2",
		defaultName: "Proxy",
		handle: {
			sources: [{ key: "source", label: "" }],
			targets: [{ key: "target", label: "" }],
		},
	},
	{
		id: "action-dataAction-v2",
		icon: <Database className="h-5 w-5 text-cyan-500" />,
		get title() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.DATA_ACTION"); },
		get description() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.PERFORM_CRUD_OPERATIONS_GET"); },
		category: "action",
		type: "dataAction",
		version: "v2",
		get defaultName() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.DATA_ACTION"); },
		handle: {
			sources: [{ key: "source", label: "" }],
			targets: [{ key: "target", label: "" }],
		},
	},
	{
		id: "action-function-v2",
		icon: <FunctionSquare className="h-5 w-5 text-primary" />,
		get title() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.FUNCTION"); },
		get description() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.RUN_A_DEPLOYED_FUNCTION"); },
		category: "action",
		type: "function",
		version: "v2",
		defaultName: "Function",
		handle: {
			sources: [{ key: "source", label: "" }],
			targets: [{ key: "target", label: "" }],
		},
	},
	{
		id: "logic-if-v2",
		icon: <GitFork className="h-5 w-5 text-success" />,
		get title() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.IF"); },
		get description() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.BRANCH_THE_WORKFLOW_BASED"); },
		type: "if",
		category: "logic",
		version: "v2",
		defaultName: "If",
		handle: {
			sources: [
				{ key: "if-true", get label() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.TRUE"); } },
				{ key: "if-false", get label() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.FALSE"); } },
			],
			targets: [{ key: "target", label: "" }],
		},
	},
	{
		id: "logic-switch-v2",
		icon: <Signpost className="h-5 w-5 text-success" />,
		get title() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.SWITCH"); },
		get description() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.ROUTE_EACH_ITEM_TO"); },
		type: "switch",
		category: "logic",
		version: "v2",
		defaultName: "Switch",
		// Outputs come from the parameters (one per rule or output); the template has only the input.
		handle: {
			sources: [],
			targets: [{ key: "target", label: "" }],
		},
		deriveHandle: deriveSwitchHandle,
		handleLabel: switchHandleLabel,
	},
	{
		id: "logic-filter-v2",
		icon: <Filter className="h-5 w-5 text-success" />,
		title: "Filter",
		description: "Keep only the items that match the conditions.",
		type: "filter",
		category: "logic",
		version: "v2",
		defaultName: "Filter",
		handle: {
			sources: [{ key: "source", label: "" }],
			targets: [{ key: "target", label: "" }],
		},
	},
	{
		id: "transform-setfield-v2",
		icon: <SquarePen className="h-5 w-5 text-blue-500" />,
		get title() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.SET_FIELD"); },
		get description() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.CREATE_OR_TRANSFORM_DATA"); },
		type: "setfield",
		category: "transform",
		version: "v2",
		get defaultName() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.SET_FIELD"); },
		handle: {
			sources: [{ key: "source", label: "" }],
			targets: [{ key: "target", label: "" }],
		},
	},
	{
		id: "transform-splitOut-v2",
		icon: <Split className="h-5 w-5 text-blue-500" />,
		get title() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.SPLIT_OUT"); },
		get description() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.TURN_A_LIST_INSIDE"); },
		type: "splitOut",
		category: "transform",
		version: "v2",
		get defaultName() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.SPLIT_OUT"); },
		handle: {
			sources: [{ key: "source", label: "" }],
			targets: [{ key: "target", label: "" }],
		},
	},
	{
		id: "transform-limit-v2",
		icon: <BetweenHorizontalStart className="h-5 w-5 text-blue-500" />,
		get title() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.LIMIT"); },
		get description() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.KEEP_AT_MOST_A"); },
		type: "limit",
		category: "transform",
		version: "v2",
		defaultName: "Limit",
		handle: {
			sources: [{ key: "source", label: "" }],
			targets: [{ key: "target", label: "" }],
		},
	},
	{
		id: "transform-merge-v2",
		icon: <Merge className="h-5 w-5 text-blue-500" />,
		get title() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.MERGE"); },
		get description() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.COMBINE_ITEMS_FROM_SEVERAL"); },
		type: "merge",
		category: "transform",
		version: "v2",
		defaultName: "Merge",
		// Inputs come from numberInputs (merge-handle-target-1..N); this template is the default 2 inputs.
		handle: {
			sources: [{ key: "source", label: "" }],
			targets: [
				{ key: "merge-handle-target-1", get label() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.INPUT_1"); } },
				{ key: "merge-handle-target-2", get label() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.INPUT_2"); } },
			],
		},
		deriveHandle: deriveMergeHandle,
	},
	{
		id: "transform-code-v2",
		icon: <Code2 className="h-5 w-5 text-blue-500" />,
		get title() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.CODE"); },
		get description() { return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "NODE_DEFINITIONS.RUN_CUSTOM_JAVASCRIPT_TO"); },
		type: "code",
		category: "transform",
		version: "v2",
		defaultName: "Code",
		handle: {
			sources: [{ key: "source", label: "" }],
			targets: [{ key: "target", label: "" }],
		},
	},
];

/** "v2" → 2; anything that is not v<number> → NaN. */
const versionNumber = (version: string): number =>
	/^v\d+$/.test(version) ? Number(version.slice(1)) : Number.NaN;

/** A definition serves stored nodes at its own version and every lower one (a v2 definition renders v1 and v2). */
export const servesVersion = (definitionVersion: string, version: string): boolean =>
	versionNumber(version) >= 1 && versionNumber(version) <= versionNumber(definitionVersion);

export const getNodeDefinition = (
	category: string,
	type: string,
	version: string,
): WorkflowNodeDefinition | undefined => {
	return NodeDefinitions.find(
		(def) =>
			def.category === category &&
			def.type === type &&
			servesVersion(def.version, version),
	);
};
