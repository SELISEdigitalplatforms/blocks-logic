import { FunctionTemplate } from "../types/function.types";
import { tStatic } from "@seliseblocks/genesis-os";

const LOGIC_FUNCTIONS_MODULE = "logic.functions";

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
    get label() {
      return tStatic(LOGIC_FUNCTIONS_MODULE, "TEMPLATES.MINIMAL_HANDLER");
    },
    get description() {
      return tStatic(LOGIC_FUNCTIONS_MODULE, "TEMPLATES.AN_EMPTY_HANDLER_THAT");
    },
  },
  {
    value: "HttpEcho",
    get label() {
      return tStatic(LOGIC_FUNCTIONS_MODULE, "TEMPLATES.HTTP_ECHO");
    },
    get description() {
      return tStatic(LOGIC_FUNCTIONS_MODULE, "TEMPLATES.ECHOES_THE_REQUEST_BACK");
    },
  },
  {
    value: "FetchTransform",
    get label() {
      return tStatic(LOGIC_FUNCTIONS_MODULE, "TEMPLATES.FETCH_TRANSFORM");
    },
    get description() {
      return tStatic(LOGIC_FUNCTIONS_MODULE, "TEMPLATES.CALLS_AN_HTTP_API");
    },
  },
  {
    value: "ReusedConnection",
    get label() {
      return tStatic(LOGIC_FUNCTIONS_MODULE, "TEMPLATES.DATABASE_REUSED_CONNECTION");
    },
    get description() {
      return tStatic(LOGIC_FUNCTIONS_MODULE, "TEMPLATES.CONNECTS_TO_MONGODB_ONCE");
    },
  },
];
