import { Loader2, Plus, Save, X } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";

type Props = {
  isEdit: boolean;
  isPending: boolean;
  onCancel?: () => void;
};

export const ProxyFormHeader = ({ isEdit, isPending, onCancel }: Props) => (
  <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
    <div>
      <h1 className="text-2xl font-bold tracking-tight">
        {isEdit ? "Edit Proxy" : "Create Proxy"}
      </h1>
      <p className="mt-1 text-sm text-muted-foreground">
        Configure the client path, upstream endpoint, and injected request values.
      </p>
    </div>
    <div className="flex flex-wrap gap-2">
      <Button
        type="button"
        variant="outline"
        className="gap-2"
        onClick={onCancel}
        disabled={isPending}
      >
        <X className="h-4 w-4" />
        Cancel
      </Button>
      <Button type="submit" className="gap-2" disabled={isPending}>
        {isPending ? (
          <Loader2 className="h-4 w-4 animate-spin" />
        ) : isEdit ? (
          <Save className="h-4 w-4" />
        ) : (
          <Plus className="h-4 w-4" />
        )}
        Save
      </Button>
    </div>
  </div>
);
