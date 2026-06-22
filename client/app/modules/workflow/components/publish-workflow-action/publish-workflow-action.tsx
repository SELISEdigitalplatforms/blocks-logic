import { useState } from "react";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { Button } from "@/components/ui-kits/button/button";
import { ChevronDown, Loader2 } from "lucide-react";
import { useParams } from "react-router-dom";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { useCreateWorkflowVersion } from "../../hooks/use-workflow-api";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/components/ui-kits/dialog/dialog";
import { Input } from "@/components/ui-kits/input/input";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { Label } from "@/components/ui-kits/label/label";

export const PublishWorkflowAction = () => {
  const [isPublishDialogOpen, setIsPublishDialogOpen] = useState(false);
  const [publishVersionName, setPublishVersionName] = useState("");
  const [publishDescription, setPublishDescription] = useState("");

  const { id: workflowId } = useParams<{ id: string }>();
  const projectKey = useProjectStore().selectedProject?.tenantId || "";
  const { mutateAsync: createVersion, isPending } = useCreateWorkflowVersion();

  const handleOpenPublishDialog = () => {
    const id = Math.random().toString(16).substring(2, 10);
    setPublishVersionName(`Version ${id}`);
    setPublishDescription("");
    setIsPublishDialogOpen(true);
  };

  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button variant="outline" size="sm" className="gap-2">
            Publish
            <ChevronDown className="h-4 w-4" />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          <DropdownMenuItem onClick={handleOpenPublishDialog}>Publish</DropdownMenuItem>
          <DropdownMenuItem>Unpublish</DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>

      <Dialog open={isPublishDialogOpen} onOpenChange={setIsPublishDialogOpen}>
        <DialogContent className="sm:max-w-[500px]">
          <DialogHeader>
            <DialogTitle>Publish workflow</DialogTitle>
          </DialogHeader>
          <div className="grid gap-4 py-4">
            <div className="grid gap-2">
              <Label htmlFor="version-name">
                Version name <span className="text-red-500">*</span>
              </Label>
              <Input
                id="version-name"
                value={publishVersionName}
                onChange={(e) => setPublishVersionName(e.target.value)}
              />
            </div>
            <div className="grid gap-2">
              <Label htmlFor="description">Describe changes (optional)</Label>
              <Textarea
                id="description"
                value={publishDescription}
                onChange={(e) => setPublishDescription(e.target.value)}
                rows={4}
              />
            </div>
          </div>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setIsPublishDialogOpen(false)}>
              Cancel
            </Button>
            <Button
              onClick={async () => {
                if (!workflowId) return;
                try {
                  await createVersion({
                    projectKey,
                    workflowId,
                    name: publishVersionName,
                    Description: publishDescription,
                  });
                  setIsPublishDialogOpen(false);
                } catch (error) {
                  console.error(error);
                }
              }}
              disabled={!publishVersionName.trim() || isPending}
            >
              {isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
              Publish
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
};
