import { z } from "zod";
import { tStatic } from "@seliseblocks/genesis-os";

export const addWorkflowSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, { error: () => tStatic("logic.workflow", "SCHEMAS.WORKFLOW_NAME_IS_REQUIRED") }),
});

export type AddWorkflowFormValues = z.infer<typeof addWorkflowSchema>;

export const addWorkflowDefaultValues: AddWorkflowFormValues = {
  name: "",
};
