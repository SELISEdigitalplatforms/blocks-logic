import { Plus, Trash2 } from "lucide-react";
import { Input } from "@/components/ui-kits/input/input";
import { Button } from "@/components/ui-kits/button/button";
import { Label } from "@/components/ui-kits/label/label";
import { IVariableBinding } from "../../types/function.types";

type VariablesEditorProps = {
  value: IVariableBinding[];
  onChange: (value: IVariableBinding[]) => void;
};

/** Non-secret `ctx.env.KEY` bindings. Secrets never go here — see the output actions editor. */
export const VariablesEditor = ({ value, onChange }: VariablesEditorProps) => {
  const update = (index: number, partial: Partial<IVariableBinding>) => {
    onChange(value.map((v, i) => (i === index ? { ...v, ...partial } : v)));
  };
  const remove = (index: number) => onChange(value.filter((_, i) => i !== index));
  const add = () => onChange([...value, { key: "", value: "" }]);

  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between">
        <Label>Environment variables</Label>
        <Button type="button" variant="ghost" size="sm" className="h-8 gap-1 px-2 text-primary" onClick={add}>
          <Plus className="h-3.5 w-3.5" />
          Add variable
        </Button>
      </div>
      {value.length === 0 && (
        <p className="rounded-lg border border-dashed bg-background px-3 py-4 text-center text-sm text-muted-foreground">
          No variables yet. Available to the function as <code>ctx.env.KEY</code>.
        </p>
      )}
      <div className="space-y-2">
        {value.map((variable, index) => (
          <div key={index} className="flex items-center gap-2">
            <Input
              placeholder="KEY"
              className="rounded-r-none border-dashed bg-background font-mono text-xs focus-visible:ring-0 focus-visible:ring-offset-0"
              value={variable.key}
              onChange={(e) => update(index, { key: e.target.value })}
            />
            <Input
              placeholder="value"
              className="rounded-l-none border-dashed bg-background font-mono text-xs focus-visible:ring-0 focus-visible:ring-offset-0"
              value={variable.value}
              onChange={(e) => update(index, { value: e.target.value })}
            />
            <Button
              type="button"
              variant="ghost"
              size="icon"
              className="h-8 w-8 shrink-0 text-muted-foreground hover:text-error"
              onClick={() => remove(index)}
            >
              <Trash2 className="h-4 w-4" />
            </Button>
          </div>
        ))}
      </div>
    </div>
  );
};
