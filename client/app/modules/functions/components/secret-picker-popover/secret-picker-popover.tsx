import { useState } from "react";
import { KeyRound } from "lucide-react";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui-kits/popover/popover";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import { useGetSecretCatalog } from "../../hooks/use-secret-catalog";

type SecretPickerPopoverProps = {
  /** Called with the `{{secret.NAME}}` placeholder to insert into the caller's field. */
  onInsert: (placeholder: string) => void;
};

/** Only ever inserts a `{{secret.NAME}}` placeholder — the value itself is resolved server-side, never seen here. */
export const SecretPickerPopover = ({ onInsert }: SecretPickerPopoverProps) => {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
  const { data: secrets = [], isLoading } = useGetSecretCatalog();

  const filtered = secrets.filter((s) => s.name.toLowerCase().includes(search.toLowerCase()));

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button type="button" variant="outline" size="sm" className="h-8 gap-1.5 px-2 text-xs">
          <KeyRound className="h-3.5 w-3.5" />
          Insert secret
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-64 p-0" align="start">
        <div className="border-b p-2">
          <Input
            placeholder="Search secrets…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="h-8 text-xs"
          />
        </div>
        <div className="max-h-64 overflow-y-auto p-1">
          {isLoading && (
            <p className="px-2 py-3 text-center text-xs text-muted-foreground">Loading…</p>
          )}
          {!isLoading && filtered.length === 0 && (
            <p className="px-2 py-3 text-center text-xs text-muted-foreground">No secrets found.</p>
          )}
          {filtered.map((secret) => (
            <button
              key={secret.id}
              type="button"
              className="w-full rounded-sm px-2 py-1.5 text-left font-mono text-xs hover:bg-accent"
              onClick={() => {
                onInsert(`{{secret.${secret.name}}}`);
                setOpen(false);
              }}
            >
              {secret.name}
            </button>
          ))}
        </div>
      </PopoverContent>
    </Popover>
  );
};
