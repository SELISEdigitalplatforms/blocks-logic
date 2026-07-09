"use client";

import { FieldProps } from "../form-field.types";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { MarkdownComponentsMap } from "./display-field";
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/components/ui-kits/accordion/accordion";
import { cn } from "@/lib/utils";
import { Info } from "lucide-react";

import React from "react";

export const CalloutAccordionDisplayField = ({
  field: { className },
  value,
}: FieldProps<{ title?: React.ReactNode; description?: React.ReactNode }>) => {
  if (!value || typeof value !== "object") {
    return null;
  }

  const { title = "", description = "" } = value as {
    title?: React.ReactNode;
    description?: React.ReactNode;
  };

  const renderContent = (content?: React.ReactNode, isTitle = false) => {
    if (!content) return null;
    if (typeof content === "string") {
      return (
        <ReactMarkdown
          remarkPlugins={[remarkGfm]}
          components={
            isTitle
              ? {
                  ...MarkdownComponentsMap,
                  p: (props) => (
                    <span className="break-words whitespace-pre-wrap">{props.children}</span>
                  ),
                }
              : MarkdownComponentsMap
          }
        >
          {content}
        </ReactMarkdown>
      );
    }
    return content;
  };

  return (
    <Accordion type="single" collapsible className={cn("w-full", className)}>
      <AccordionItem
        value="item-1"
        className="rounded-lg border bg-slate-50/50 px-4 dark:border-blue-900/50 dark:bg-blue-950/20"
      >
        <AccordionTrigger className="py-3 hover:no-underline">
          <div className="flex items-center gap-2 text-left">
            <Info className="h-4 w-4 shrink-0 text-slate-500 dark:text-blue-500" />
            <div className="text-sm font-medium text-slate-700 dark:text-blue-400">
              {renderContent(title, true)}
            </div>
          </div>
        </AccordionTrigger>
        <AccordionContent className="pb-3 text-sm text-slate-600 dark:text-blue-300">
          {renderContent(description)}
        </AccordionContent>
      </AccordionItem>
    </Accordion>
  );
};
