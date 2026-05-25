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
} from "lucide-react";

export const NodeDefinitions: WorkflowNodeDefinition[] = [
	{
		id: "trigger-webhook-v1",
		icon: <Webhook className="h-5 w-5 text-error" />,
		title: "Webhook",
		description:
			"Start the workflow by clicking the Execute button. Ideal for quick tests and simple runs.",
		type: "webhook",
		category: "trigger",
		version: "v1",
		defaultName: "Webhook",
		handleSpec: {
			source: ["source"],
			target: [],
		},
	},
	{
		id: "trigger-email-v1",
		icon: <Inbox className="h-5 w-5 text-success" />,
		title: "Email Trigger",
		description: "Triggers the workflow when a new email is received",
		type: "email",
		category: "trigger",
		version: "v1",
		defaultName: "Email Trigger",
		handleSpec: {
			source: ["source"],
			target: [],
		},
	},
	{
		id: "trigger-dataGateway-v1",
		icon: <DatabaseZap className="h-5 w-5 text-red-500" />,
		title: "Data Trigger",
		description:
			"Triggers the workflow when data is inserted, updated, or deleted in a collection",
		type: "dataGateway",
		category: "trigger",
		version: "v1",
		defaultName: "Data Trigger",
		handleSpec: {
			source: ["source"],
			target: [],
		},
	},
	{
		id: "trigger-blockschedule-v1",
		icon: <Clock className="h-5 w-5 text-rose-500" />,
		title: "Blocks Schedule",
		description: "Triggers the workflow based on a scheduled block of time",
		type: "blockschedule",
		category: "trigger",
		version: "v1",
		isComingSoon: true,
		defaultName: "Blocks Schedule",
		handleSpec: {
			source: ["source"],
			target: [],
		},
	},
	{
		id: "action-agent-v1",
		icon: <Bot className="h-5 w-5 text-orange-500" />,
		title: "Agent",
		description:
			"Executes an AI-powered agent to perform reasoning, decision-making, or task automation within the workflow.",
		category: "action",
		type: "agent",
		version: "v1",
		defaultName: "Agent",
		handleSpec: {
			source: ["source"],
			target: ["target"],
		},
	},
	{
		id: "action-sendMail-v1",
		icon: <Mail className="h-5 w-5 text-purple-500" />,
		title: "Send Mail",
		description: "Sends an email as part of the workflow process.",
		category: "action",
		type: "sendMail",
		version: "v1",
		defaultName: "Send Mail",
		handleSpec: {
			source: ["source"],
			target: ["target"],
		},
	},
	{
		id: "action-httpRequest-v1",
		icon: <Globe className="h-5 w-5 text-indigo-500" />,
		title: "HTTP Request",
		description: "Sends an HTTP request as part of the workflow process.",
		category: "action",
		type: "httpRequest",
		version: "v1",
		defaultName: "HTTP Request",
		handleSpec: {
			source: ["source"],
			target: ["target"],
		},
	},
	{
		id: "action-dataAction-v1",
		icon: <Database className="h-5 w-5 text-cyan-500" />,
		title: "Data Action",
		description:
			"Perform CRUD operations (Get, Insert, Update, Delete) on data collections.",
		category: "action",
		type: "dataAction",
		version: "v1",
		defaultName: "Data Action",
		handleSpec: {
			source: ["source"],
			target: ["target"],
		},
	},
	{
		id: "logic-if-v1",
		icon: <GitFork className="h-5 w-5 text-success" />,
		title: "If",
		description:
			"Branch the workflow based on a condition (true/false outputs).",
		type: "if",
		category: "logic",
		version: "v1",
		defaultName: "If",
		handleSpec: {
			source: ["if-true", "if-false"],
			target: ["target"],
		},
	},
	{
		id: "transform-setfield-v1",
		icon: <SquarePen className="h-5 w-5 text-blue-500" />,
		title: "Set Field",
		description: "Create or transform data fields for subsequent steps.",
		type: "setfield",
		category: "transform",
		version: "v1",
		isComingSoon: true,
		defaultName: "Set Field",
		handleSpec: {
			source: ["source"],
			target: ["target"],
		},
	},
];

export const getNodeDefinition = (
	category: string,
	type: string,
	version: string,
) => {
	return NodeDefinitions.find(
		(def) =>
			def.category === category &&
			def.type === type &&
			def.version === version,
	);
};
