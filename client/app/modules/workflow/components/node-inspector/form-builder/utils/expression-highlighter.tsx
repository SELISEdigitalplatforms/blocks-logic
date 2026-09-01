import React, { useCallback, useEffect, useLayoutEffect, useRef } from "react";
import { WorkflowNode } from "../../../../models/node.model";
import { useWorkflow } from "../../../../hooks/use-workflow";

const validateExpression = (content: string, nodes: WorkflowNode[]): boolean => {
  // Check if it's the simple json.input format
  if (/^json\.input(?:(?:\.[a-zA-Z0-9_]+|\[\d+\]|\["[^"]+"\]|\['[^']+'\])*)$/.test(content)) {
    return true;
  }

  // Check if it's the simple json.output format
  if (/^json\.output(?:(?:\.[a-zA-Z0-9_]+|\[\d+\]|\["[^"]+"\]|\['[^']+'\])*)$/.test(content)) {
    return true;
  }

  // Check if it's the node format for json.input
  const nodeInputMatch = content.match(
    /^node\["([^"]+)"\]\.json\.input(?:(?:\.[a-zA-Z0-9_]+|\[\d+\]|\["[^"]+"\]|\['[^']+'\])*)$/,
  );
  if (nodeInputMatch) {
    const nodeName = nodeInputMatch[1];
    return nodes.some(
      (n) => n.name === nodeName || n.data?.name === nodeName || n.data?.label === nodeName,
    );
  }

  // Check if it's the node format for json.output
  const nodeMatch = content.match(
    /^node\["([^"]+)"\]\.json\.output(?:(?:\.[a-zA-Z0-9_]+|\[\d+\]|\["[^"]+"\]|\['[^']+'\])*)$/,
  );
  if (nodeMatch) {
    const nodeName = nodeMatch[1];
    return nodes.some(
      (n) => n.name === nodeName || n.data?.name === nodeName || n.data?.label === nodeName,
    );
  }

  return false;
};

const renderHighlightedText = (
  text: string,
  nodes: WorkflowNode[],
  isMultiline: boolean,
): React.ReactNode => {
  if (!text) return null;

  const regex = /({{\$.*?}})/g;
  const parts = text.split(regex);

  const rendered = parts.map((part, index) => {
    if (part.startsWith("{{$") && part.endsWith("}}")) {
      const content = part.slice(3, -2);
      const isValid = validateExpression(content, nodes);

      return (
        <span
          key={index}
          className={
            isValid
              ? "text-green-700 dark:text-green-300 bg-green-500/10 dark:bg-green-500/25 shadow-[inset_0_0_0_1px_rgba(34,197,94,0.55)] rounded-sm"
              : "text-red-700 dark:text-red-300 bg-red-500/10 dark:bg-red-500/25 shadow-[inset_0_0_0_1px_rgba(239,68,68,0.55)] rounded-sm"
          }
        >
          {part}
        </span>
      );
    }
    return <span key={index}>{part}</span>;
  });

  // A trailing newline is collapsed by block layout but not by the textarea, which would
  // put the backdrop one line short. Pad it back.
  if (isMultiline && text.endsWith("\n")) {
    rendered.push(<span key="trailing-newline">{"\n"}</span>);
  }

  return rendered;
};

/**
 * Every computed property that can change where a glyph lands. The backdrop copies these
 * from the live control instead of restating them as classes, so the two layers cannot
 * drift when the control's own styling, font loading, or zoom level changes.
 */
const MIRRORED_STYLE_PROPS = [
  "fontFamily",
  "fontSize",
  "fontWeight",
  "fontStyle",
  "fontVariant",
  "fontStretch",
  "fontFeatureSettings",
  "fontVariationSettings",
  "fontKerning",
  "lineHeight",
  "letterSpacing",
  "wordSpacing",
  "textIndent",
  "textTransform",
  "textAlign",
  "textRendering",
  "direction",
  "whiteSpace",
  "wordBreak",
  "overflowWrap",
  "tabSize",
  "paddingTop",
  "paddingRight",
  "paddingBottom",
  "paddingLeft",
  "borderTopLeftRadius",
  "borderTopRightRadius",
  "borderBottomLeftRadius",
  "borderBottomRightRadius",
] as const;

const SELECTION_STYLE_ID = "expression-highlighter-selection";

// The control sits on top with transparent text, so its selection highlight would paint
// over the backdrop. A translucent selection colour lets the coloured text read through.
const ensureSelectionStyles = () => {
  if (typeof document === "undefined" || document.getElementById(SELECTION_STYLE_ID)) return;
  const style = document.createElement("style");
  style.id = SELECTION_STYLE_ID;
  style.textContent =
    ".expression-highlighter-control::selection { background-color: rgba(59, 130, 246, 0.35); }";
  document.head.appendChild(style);
};

type ChildProps = {
  onScroll?: React.UIEventHandler<HTMLElement>;
  className?: string;
  style?: React.CSSProperties;
};

type Control = HTMLInputElement | HTMLTextAreaElement;

export const ExpressionHighlighter = ({
  children,
  value,
  isMultiline = true,
  fontClassName = "",
  disableHighlighting = false,
}: {
  children: React.ReactElement<ChildProps>;
  value: string;
  isMultiline?: boolean;
  /** @deprecated the backdrop now copies the control's computed font. */
  fontClassName?: string;
  disableHighlighting?: boolean;
}) => {
  const { nodes } = useWorkflow();
  const backdropRef = useRef<HTMLDivElement | null>(null);
  const controlRef = useRef<Control | null>(null);

  /** Give the backdrop the control's exact content box and text metrics. */
  const syncBackdrop = useCallback(() => {
    const control = controlRef.current;
    const backdrop = backdropRef.current;
    if (!control || !backdrop) return;

    const computed = window.getComputedStyle(control);
    MIRRORED_STYLE_PROPS.forEach((prop) => {
      backdrop.style[prop] = computed[prop];
    });

    // clientWidth/clientHeight exclude the border and any scrollbar, so offsetting the
    // backdrop by the border width leaves it with a content box identical to the
    // control's -- which is what makes soft wrapping break line for line the same way.
    backdrop.style.top = computed.borderTopWidth;
    backdrop.style.left = computed.borderLeftWidth;
    backdrop.style.width = `${control.clientWidth}px`;
    backdrop.style.height = `${control.clientHeight}px`;

    backdrop.scrollTop = control.scrollTop;
    backdrop.scrollLeft = control.scrollLeft;
  }, []);

  const attachControl = useCallback(
    (node: Control | null) => {
      controlRef.current = node;
      if (node) syncBackdrop();
    },
    [syncBackdrop],
  );

  useEffect(() => {
    ensureSelectionStyles();
  }, []);

  useLayoutEffect(() => {
    if (disableHighlighting) return;
    const control = controlRef.current;
    if (!control) return;

    syncBackdrop();

    // The caret can scroll the control without firing a scroll event, so re-sync on
    // anything else that can move it.
    const events = ["scroll", "input", "keyup", "click", "select", "focus", "blur"];
    events.forEach((event) => control.addEventListener(event, syncBackdrop));

    const resizeObserver = new ResizeObserver(syncBackdrop);
    resizeObserver.observe(control);

    // A late-loading webfont re-measures the control; the backdrop has to follow.
    let cancelled = false;
    document.fonts?.ready.then(() => {
      if (!cancelled) syncBackdrop();
    });

    return () => {
      cancelled = true;
      events.forEach((event) => control.removeEventListener(event, syncBackdrop));
      resizeObserver.disconnect();
    };
  }, [disableHighlighting, syncBackdrop]);

  useLayoutEffect(() => {
    if (!disableHighlighting) syncBackdrop();
  }, [value, disableHighlighting, syncBackdrop]);

  if (disableHighlighting) {
    return children;
  }

  const childRef = (children as unknown as { ref?: React.Ref<Control> }).ref;

  const enhancedChild = React.cloneElement(children, {
    spellCheck: false,
    ref: (node: Control | null) => {
      attachControl(node);
      if (typeof childRef === "function") childRef(node);
      else if (childRef && typeof childRef === "object")
        (childRef as React.MutableRefObject<Control | null>).current = node;
    },
    onScroll: (e: React.UIEvent<HTMLElement>) => {
      syncBackdrop();
      children.props.onScroll?.(e);
    },
    className:
      `${children.props.className || ""} expression-highlighter-control relative z-10 caret-black dark:caret-white`.trim(),
    // Inline so it beats the control's own utility classes: the text is invisible, the
    // caret is not, and the coloured backdrop shows through.
    style: {
      ...children.props.style,
      color: "transparent",
      backgroundColor: "transparent",
    },
  } as Partial<ChildProps>);

  return (
    <div className={`relative w-full bg-background rounded-md ${fontClassName}`.trim()}>
      <div
        ref={backdropRef}
        className="absolute overflow-hidden pointer-events-none z-0 box-border m-0 text-foreground"
        style={isMultiline ? undefined : { display: "flex", alignItems: "center" }}
        aria-hidden="true"
      >
        {isMultiline ? (
          renderHighlightedText(value, nodes, true)
        ) : (
          <span className="flex-none">{renderHighlightedText(value, nodes, false)}</span>
        )}
      </div>
      {enhancedChild}
    </div>
  );
};
