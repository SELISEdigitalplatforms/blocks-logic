import { z } from "zod";
import { tStatic } from "@seliseblocks/genesis-os";

export const duplicateWorkflowSchema = z.object({
  name: z
    .string()
    .min(1, { error: () => tStatic("logic.workflow", "SCHEMAS.WORKFLOW_NAME_IS_REQUIRED") }),
});

export type DuplicateWorkflowFormValues = z.infer<typeof duplicateWorkflowSchema>;
