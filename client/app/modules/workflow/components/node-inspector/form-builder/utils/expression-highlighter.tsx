import React, { useRef } from "react";
import { WorkflowNode } from "../../../../models/node.model";
import { useWorkflow } from "../../../../hooks/use-workflow";

const validateExpression = (
	content: string,
	nodes: WorkflowNode[],
): boolean => {
	// Allow simple json.input exactly
	if (content === "json.input") {
		return true;
	}

	// Check if it's the simple json.output format (optionally followed by parameters)
	const simpleMatch = content.match(
		/^json\.output(?:\.([a-zA-Z0-9_]+(?:\.[a-zA-Z0-9_]+)*))?$/,
	);
	if (simpleMatch) {
		return true;
	}

	// Allow node json.input exactly
	const nodeInputMatch = content.match(/^node\["([^"]+)"\]\.json\.input$/);
	if (nodeInputMatch) {
		const nodeName = nodeInputMatch[1];
		return nodes.some(
			(n) =>
				n.name === nodeName ||
				n.data?.name === nodeName ||
				n.data?.label === nodeName,
		);
	}

	// Check if it's the node format for json.output (optionally followed by parameters)
	const nodeMatch = content.match(
		/^node\["([^"]+)"\]\.json\.output(?:\.([a-zA-Z0-9_]+(?:\.[a-zA-Z0-9_]+)*))?$/,
	);
	if (nodeMatch) {
		const nodeName = nodeMatch[1];
		return nodes.some(
			(n) =>
				n.name === nodeName ||
				n.data?.name === nodeName ||
				n.data?.label === nodeName,
		);
	}

	return false;
};

const renderHighlightedText = (
	text: string,
	nodes: WorkflowNode[],
): React.ReactNode => {
	if (!text) return null;

	const regex = /({{\$.*?}})/g;
	const parts = text.split(regex);

	return parts.map((part, index) => {
		if (part.startsWith("{{$") && part.endsWith("}}")) {
			const content = part.slice(3, -2);
			const isValid = validateExpression(content, nodes);

			return (
				<span
					key={index}
					className={
						isValid
							? "text-green-600 dark:text-green-400 bg-green-500/10 dark:bg-green-500/10 ring-1 ring-green-500/30 dark:ring-green-400/30 rounded-sm"
							: "text-red-600 dark:text-red-400 bg-red-500/10 dark:bg-red-500/10 ring-1 ring-red-500/30 dark:ring-red-400/30 rounded-sm"
					}
				>
					{part}
				</span>
			);
		}
		return <span key={index}>{part}</span>;
	});
};

export const ExpressionHighlighter = ({
	children,
	value,
	isMultiline = true,
	fontClassName = "",
	disableHighlighting = false,
}: {
	children: React.ReactElement;
	value: string;
	isMultiline?: boolean;
	fontClassName?: string;
	disableHighlighting?: boolean;
}) => {
	if (disableHighlighting) {
		return children;
	}
	const { nodes } = useWorkflow();
	const backdropRef = useRef<HTMLDivElement>(null);

	const handleScroll: React.UIEventHandler<HTMLElement> = (e) => {
		if (backdropRef.current) {
			backdropRef.current.scrollTop = e.currentTarget.scrollTop;
			backdropRef.current.scrollLeft = e.currentTarget.scrollLeft;
		}
	};

	const enhancedChild = React.cloneElement(children, {
		onScroll: (e: React.UIEvent<HTMLElement>) => {
			handleScroll(e);
			if (children.props.onScroll) {
				children.props.onScroll(e);
			}
		},
		// We add !text-transparent and !bg-transparent to ensure they override existing tailwind classes like bg-background
		className:
			`${children.props.className || ""} relative z-10 !bg-transparent !text-transparent caret-black dark:caret-white`.trim(),
	});

	return (
		<div className="relative w-full bg-background rounded-md">
			<div
				ref={backdropRef}
				className={`absolute inset-0 pointer-events-none overflow-hidden rounded-md border border-transparent px-3 py-2 text-sm z-0 text-foreground ${isMultiline ? "whitespace-pre-wrap break-words" : "whitespace-pre"} ${fontClassName}`}
				aria-hidden="true"
			>
				{renderHighlightedText(value, nodes)}
				{value?.endsWith("\n") && isMultiline ? <br /> : null}
			</div>
			{enhancedChild}
		</div>
	);
};
