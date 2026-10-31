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
	Bell,
} from "lucide-react";
import { deriveMergeHandle } from "@blocks-workflow/utils/merge-handles";
import { deriveSwitchHandle, switchHandleLabel } from "@blocks-workflow/utils/switch-handles";

export const NodeDefinitions: WorkflowNodeDefinition[] = [
	{
		id: "trigger-webhook-v2",
		icon: <Webhook className="h-5 w-5 text-error" />,
		title: "Webhook",
		description:
			"Starts the workflow when another app sends a POST request to its URL.",
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
		title: "Email Trigger",
		description: "Triggers the workflow when a new email is received",
		type: "email",
		category: "trigger",
		version: "v2",
		defaultName: "Email Trigger",
		handle: {
			sources: [{ key: "source", label: "" }],
			targets: [],
		},
	},
	{
		id: "trigger-dataGateway-v2",
		icon: <DatabaseZap className="h-5 w-5 text-red-500" />,
		title: "Data Trigger",
		description:
			"Triggers the workflow when data is inserted, updated, or deleted in a collection",
		type: "dataGateway",
		category: "trigger",
		version: "v2",
		defaultName: "Data Trigger",
		handle: {
			sources: [{ key: "source", label: "" }],
			targets: [],
		},
	},
	{
		id: "trigger-schedule-v2",
		icon: <Clock className="h-5 w-5 text-rose-500" />,
		title: "Schedule",
		description: "Triggers the workflow on a schedule (cron)",
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
		title: "Agent",
		description:
			"Executes an AI-powered agent to perform reasoning, decision-making, or task automation within the workflow.",
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
		title: "Send Mail",
		description: "Sends an email as part of the workflow process.",
		category: "action",
		type: "sendMail",
		version: "v2",
		defaultName: "Send Mail",
		handle: {
			sources: [{ key: "source", label: "" }],
			targets: [{ key: "target", label: "" }],
		},
	},
	{
		id: "action-httpRequest-v2",
		icon: <Globe className="h-5 w-5 text-indigo-500" />,
		title: "HTTP Request",
		description: "Sends an HTTP request as part of the workflow process.",
		category: "action",
		type: "httpRequest",
		version: "v2",
		defaultName: "HTTP Request",
		handle: {
			sources: [{ key: "source", label: "" }],
			targets: [{ key: "target", label: "" }],
		},
	},
	{
		id: "action-proxy-v2",
		icon: <Waypoints className="h-5 w-5 text-teal-500" />,
		title: "Proxy",
		description:
			"Call a configured proxy. The upstream URL and its credentials stay in the proxy, never in the workflow.",
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
		id: "action-notification-v2",
		icon: <Bell className="h-5 w-5 text-amber-500" />,
		title: "Send Notification",
		description:
			"Sends a Blocks notification to users through one of the project's notification configurations.",
		type: "notification",
		category: "action",
		version: "v2",
		defaultName: "Send Notification",
		handle: {
			sources: [{ key: "source", label: "" }],
			targets: [{ key: "target", label: "" }],
		},
	},
	{
		id: "action-dataAction-v2",
		icon: <Database className="h-5 w-5 text-cyan-500" />,
		title: "Data Action",
		description:
			"Perform CRUD operations (Get, Insert, Update, Delete) on data collections.",
		category: "action",
		type: "dataAction",
		version: "v2",
		defaultName: "Data Action",
		handle: {
			sources: [{ key: "source", label: "" }],
			targets: [{ key: "target", label: "" }],
		},
	},
	{
		id: "action-function-v2",
		icon: <FunctionSquare className="h-5 w-5 text-primary" />,
		title: "Function",
		description:
			"Run a deployed function and pass its result to the next node.",
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
		title: "If",
		description:
			"Branch the workflow based on a condition (true/false outputs).",
		type: "if",
		category: "logic",
		version: "v2",
		defaultName: "If",
		handle: {
			sources: [
				{ key: "if-true", label: "True" },
				{ key: "if-false", label: "False" },
			],
			targets: [{ key: "target", label: "" }],
		},
	},
	{
		id: "logic-switch-v2",
		icon: <Signpost className="h-5 w-5 text-success" />,
		title: "Switch",
		description:
			"Route each item to one of many outputs by rules.",
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
		id: "transform-setfield-v2",
		icon: <SquarePen className="h-5 w-5 text-blue-500" />,
		title: "Set Field",
		description: "Create or transform data fields for subsequent steps.",
		type: "setfield",
		category: "transform",
		version: "v2",
		defaultName: "Set Field",
		handle: {
			sources: [{ key: "source", label: "" }],
			targets: [{ key: "target", label: "" }],
		},
	},
	{
		id: "transform-splitOut-v2",
		icon: <Split className="h-5 w-5 text-blue-500" />,
		title: "Split Out",
		description: "Turn a list inside each item into separate items.",
		type: "splitOut",
		category: "transform",
		version: "v2",
		defaultName: "Split Out",
		handle: {
			sources: [{ key: "source", label: "" }],
			targets: [{ key: "target", label: "" }],
		},
	},
	{
		id: "transform-merge-v2",
		icon: <Merge className="h-5 w-5 text-blue-500" />,
		title: "Merge",
		description: "Combine items from several inputs into one list.",
		type: "merge",
		category: "transform",
		version: "v2",
		defaultName: "Merge",
		// Inputs come from numberInputs (merge-handle-target-1..N); this template is the default 2 inputs.
		handle: {
			sources: [{ key: "source", label: "" }],
			targets: [
				{ key: "merge-handle-target-1", label: "Input 1" },
				{ key: "merge-handle-target-2", label: "Input 2" },
			],
		},
		deriveHandle: deriveMergeHandle,
	},
	{
		id: "transform-code-v2",
		icon: <Code2 className="h-5 w-5 text-blue-500" />,
		title: "Code",
		description:
			"Run custom JavaScript to transform data or implement custom logic.",
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
