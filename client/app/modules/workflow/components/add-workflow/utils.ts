import { z } from "zod";

export const addWorkflowSchema = z.object({
  name: z.string().trim().min(1, "Workflow name is required"),
});

export type AddWorkflowFormValues = z.infer<typeof addWorkflowSchema>;

export const addWorkflowDefaultValues: AddWorkflowFormValues = {
  name: "",
};
