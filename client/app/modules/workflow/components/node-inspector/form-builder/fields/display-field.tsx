"use client";

import { FieldProps } from "../form-field.types";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import type { Components } from "react-markdown";
import { cn } from "@/lib/utils";

export const MarkdownComponentsMap: Partial<Components> = {
  p: (props) => (
    <p className="my-1 whitespace-pre-wrap break-words leading-relaxed">{props.children}</p>
  ),

  a: (props) => (
    <a className="text-primary" target="_blank" {...props}>
      {props.children}
    </a>
  ),
  table: (props) => (
    <div className="w-full overflow-x-auto">
      <table className="w-full border-collapse border">{props.children}</table>
    </div>
  ),

  th: (props) => (
    <th className="min-w-[150px] max-w-[350px] break-all border p-2">{props.children}</th>
  ),
  td: (props) => (
    <td className="min-w-[150px] max-w-[350px] break-words border p-2">{props.children}</td>
  ),
};

export const DisplayField = ({ field: { className },value }: FieldProps<string>) => {
  return (
    <div className={cn("rounded-sm border p-4", className)}>
      <ReactMarkdown remarkPlugins={[remarkGfm]} components={MarkdownComponentsMap}>
        {value}
      </ReactMarkdown>
    </div>
  );
};
