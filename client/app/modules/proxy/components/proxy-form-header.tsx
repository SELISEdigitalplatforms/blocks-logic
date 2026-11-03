import { Loader2, Plus, Save, X } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { useT } from "@seliseblocks/genesis-os";

type Props = {
  isEdit: boolean;
  isPending: boolean;
  onCancel?: () => void;
};

export const ProxyFormHeader = ({ isEdit, isPending, onCancel }: Props) => {
  const { t } = useT("logic.proxy.components");
  let submitIcon = <Plus className="h-4 w-4" />;
  if (isPending) submitIcon = <Loader2 className="h-4 w-4 animate-spin" />;
  else if (isEdit) submitIcon = <Save className="h-4 w-4" />;
  return (
    <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
      <div>
        <h1 className="text-2xl font-bold tracking-tight">
          {isEdit ? t("PROXY_FORM_HEADER.EDIT_PROXY") : t("PROXY_FORM_HEADER.CREATE_PROXY")}
        </h1>
        <p className="mt-1 text-sm text-muted-foreground">
          {t("PROXY_FORM_HEADER.CONFIGURE_THE_CLIENT_PATH")}
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
          {t("common:CANCEL")}
        </Button>
        <Button type="submit" className="gap-2" disabled={isPending}>
          {submitIcon}
          {t("common:SAVE")}
        </Button>
      </div>
    </div>
  );
};
