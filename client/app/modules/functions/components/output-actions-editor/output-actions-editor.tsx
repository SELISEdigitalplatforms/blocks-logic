import { Plus, Trash2 } from "lucide-react";
import { Input } from "@/components/ui-kits/input/input";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { Button } from "@/components/ui-kits/button/button";
import { Label } from "@/components/ui-kits/label/label";
import { Switch } from "@/components/ui-kits/switch/switch";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { HTTP_METHOD_OPTIONS } from "../../constants/limits.constant";
import { IOutputAction } from "../../types/function.types";
import { SecretPickerPopover } from "../secret-picker-popover";

type OutputActionsEditorProps = {
  value: IOutputAction[];
  onChange: (value: IOutputAction[]) => void;
};

const newOutputAction = (): IOutputAction => ({
  id: crypto.randomUUID(),
  kind: "ExternalHttp",
  enabled: true,
  url: "",
  method: "POST",
  headers: {},
  bodyTemplate: null,
  timeoutSeconds: 30,
});

export const OutputActionsEditor = ({ value, onChange }: OutputActionsEditorProps) => {
  const update = (index: number, partial: Partial<IOutputAction>) => {
    onChange(value.map((action, i) => (i === index ? { ...action, ...partial } : action)));
  };
  const remove = (index: number) => onChange(value.filter((_, i) => i !== index));
  const add = () => onChange([...value, newOutputAction()]);

  const updateHeader = (index: number, key: string, headerValue: string) => {
    update(index, { headers: { ...value[index].headers, [key]: headerValue } });
  };
  const removeHeader = (index: number, key: string) => {
    const headers = { ...value[index].headers };
    delete headers[key];
    update(index, { headers });
  };
  const addHeader = (index: number) => updateHeader(index, "", "");

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <Label>Output actions</Label>
          <p className="text-xs text-muted-foreground">
            Run in order after a successful run. Use <code>{"{{result}}"}</code> in the body for the
            run&apos;s result JSON, and <code>{"{{secret.NAME}}"}</code> for a resolved secret value.
          </p>
        </div>
        <Button type="button" variant="outline" size="sm" className="gap-1.5" onClick={add}>
          <Plus className="h-3.5 w-3.5" />
          Add action
        </Button>
      </div>

      {value.length === 0 && (
        <p className="rounded-lg border border-dashed bg-background px-3 py-4 text-center text-sm text-muted-foreground">
          No output actions configured. A successful run&apos;s result is not sent anywhere.
        </p>
      )}

      {value.map((action, index) => (
        <Card key={action.id}>
          <CardHeader className="flex flex-row items-center justify-between gap-3 pb-3">
            <div className="flex items-center gap-2">
              <Switch
                size="sm"
                checked={action.enabled}
                onCheckedChange={(checked) => update(index, { enabled: checked })}
              />
              <span className="text-sm font-medium">Action {index + 1}</span>
            </div>
            <Button
              type="button"
              variant="ghost"
              size="icon"
              aria-label="Remove action"
              className="h-7 w-7 text-muted-foreground hover:text-error"
              onClick={() => remove(index)}
            >
              <Trash2 className="h-3.5 w-3.5" />
            </Button>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="flex items-start gap-3">
              <Select value={action.method} onValueChange={(v) => update(index, { method: v })}>
                <SelectTrigger className="w-28 shrink-0">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {HTTP_METHOD_OPTIONS.map((method) => (
                    <SelectItem key={method} value={method}>
                      {method}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <Input
                placeholder="https://example.com/webhook"
                value={action.url}
                onChange={(e) => update(index, { url: e.target.value })}
                className="flex-1"
              />
              <Input
                type="number"
                min={1}
                className="w-24 shrink-0"
                value={action.timeoutSeconds}
                onChange={(e) => update(index, { timeoutSeconds: Number(e.target.value) })}
                title="Timeout (seconds)"
              />
            </div>

            <div className="space-y-2">
              <div className="flex items-center justify-between">
                <Label className="text-xs">Headers</Label>
                <div className="flex items-center gap-2">
                  <SecretPickerPopover
                    onInsert={(placeholder) => updateHeader(index, "Authorization", placeholder)}
                  />
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    className="h-8 gap-1 px-2 text-primary"
                    onClick={() => addHeader(index)}
                  >
                    <Plus className="h-3.5 w-3.5" />
                    Add header
                  </Button>
                </div>
              </div>
              {Object.entries(action.headers).map(([key, headerValue]) => (
                <div key={key} className="flex items-center gap-2">
                  <Input
                    placeholder="Header"
                    className="rounded-r-none border-dashed bg-background font-mono text-xs"
                    value={key}
                    onChange={(e) => {
                      const headers = { ...action.headers };
                      delete headers[key];
                      headers[e.target.value] = headerValue;
                      update(index, { headers });
                    }}
                  />
                  <Input
                    placeholder="Value"
                    className="rounded-l-none border-dashed bg-background font-mono text-xs"
                    value={headerValue}
                    onChange={(e) => updateHeader(index, key, e.target.value)}
                  />
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    className="h-8 w-8 shrink-0 text-muted-foreground hover:text-error"
                    onClick={() => removeHeader(index, key)}
                  >
                    <Trash2 className="h-4 w-4" />
                  </Button>
                </div>
              ))}
            </div>

            <div className="space-y-1.5">
              <div className="flex items-center justify-between">
                <Label className="text-xs">
                  Body <span className="font-normal text-muted-foreground">optional — defaults to the result JSON</span>
                </Label>
                <SecretPickerPopover
                  onInsert={(placeholder) =>
                    update(index, { bodyTemplate: `${action.bodyTemplate ?? ""}${placeholder}` })
                  }
                />
              </div>
              <Textarea
                className="min-h-[80px] resize-none font-mono text-xs"
                placeholder={"{{result}}"}
                value={action.bodyTemplate ?? ""}
                onChange={(e) => update(index, { bodyTemplate: e.target.value || null })}
              />
            </div>
          </CardContent>
        </Card>
      ))}
    </div>
  );
};
