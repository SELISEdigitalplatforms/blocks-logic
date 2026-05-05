import { z } from "zod";

export const duplicateWorkflowSchema = z.object({
  name: z.string().min(1, "Workflow name is required"),
});

export type DuplicateWorkflowFormValues = z.infer<typeof duplicateWorkflowSchema>;
