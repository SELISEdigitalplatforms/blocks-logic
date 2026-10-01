import { FunctionTemplate } from "../types/function.types";

/**
 * The starters offered by the create dialog. The source itself lives server-side
 * (`FunctionStarterTemplates`) so a function is created with its code in one round trip; this is
 * only what the picker shows.
 */
export const FUNCTION_TEMPLATES: {
  value: FunctionTemplate;
  label: string;
  description: string;
}[] = [
  {
    value: "Minimal",
    label: "Minimal handler",
    description: "An empty handler that logs its input and returns it.",
  },
  {
    value: "HttpEcho",
    label: "HTTP echo",
    description: "Echoes the request back with the caller's identity from ctx.context.",
  },
  {
    value: "FetchTransform",
    label: "Fetch & transform",
    description: "Calls an HTTP API with fetch(), reshapes the response and returns it.",
  },
];
