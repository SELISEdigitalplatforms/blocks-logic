import { ArrowDown, ArrowUp, Plus, Trash2 } from "lucide-react";
import { Input } from "@/components/ui-kits/input/input";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { Button } from "@/components/ui-kits/button/button";
import { Label } from "@/components/ui-kits/label/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { Card } from "@/components/ui-kits/card/card";
import { cn } from "@/lib/utils";
import { HTTP_METHOD_OPTIONS } from "../../constants/limits.constant";
import { IOutputAction } from "../../types/function.types";
import { SecretPickerPopover } from "../secret-picker-popover";

type OutputActionsEditorProps = {
  value: IOutputAction[];
  onChange: (value: IOutputAction[]) => void;
};

const TIMEOUT_OPTIONS = [5, 10, 15, 20, 30];

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

/**
 * Output actions run on the Blocks host after the run finishes, which is the whole point of the tab:
 * they are the only place a secret is resolved, and the sandbox never sees one. A `{{secret.NAME}}`
 * placeholder is all this editor ever holds.
 */
export const OutputActionsEditor = ({ value, onChange }: OutputActionsEditorProps) => {
  const update = (index: number, partial: Partial<IOutputAction>) => {
    onChange(value.map((action, i) => (i === index ? { ...action, ...partial } : action)));
  };
  const remove = (index: number) => onChange(value.filter((_, i) => i !== index));
  const add = () => onChange([...value, newOutputAction()]);

  const move = (index: number, offset: number) => {
    const target = index + offset;
    if (target < 0 || target >= value.length) return;
    const next = [...value];
    [next[index], next[target]] = [next[target], next[index]];
    onChange(next);
  };

  const updateHeader = (index: number, key: string, headerValue: string) => {
    update(index, { headers: { ...value[index].headers, [key]: headerValue } });
  };
  const renameHeader = (index: number, from: string, to: string) => {
    const headers: Record<string, string> = {};
    Object.entries(value[index].headers).forEach(([key, headerValue]) => {
      headers[key === from ? to : key] = headerValue;
    });
    update(index, { headers });
  };
  const removeHeader = (index: number, key: string) => {
    const headers = { ...value[index].headers };
    delete headers[key];
    update(index, { headers });
  };

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <p className="max-w-[66ch] text-xs leading-relaxed text-medium-emphasis">
          What happens to the value the function returns. Actions run in order on the Blocks host —
          outside the sandbox — so they can use stored secrets the code never sees.
        </p>
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="shrink-0 gap-1.5"
          onClick={add}
        >
          <Plus className="h-3.5 w-3.5" />
          Add action
        </Button>
      </div>

      {value.length === 0 && (
        <div className="flex flex-col gap-1 rounded-lg border border-dashed px-5 py-8 text-center">
          <span className="text-sm font-semibold">No output actions</span>
          <span className="text-xs text-medium-emphasis">
            The return value is stored on the run and available from{" "}
            <code className="font-mono">GET /api/fn/runs/{"{runId}"}</code>.
          </span>
        </div>
      )}

      {value.map((action, index) => (
        <Card key={action.id} className="overflow-hidden">
          <div className="flex items-center gap-3 border-b bg-surface-app px-4 py-3">
            <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-blocks-primary-50 text-xs font-semibold text-primary">
              {index + 1}
            </span>
            <span className="min-w-0 truncate text-sm font-semibold">External HTTP call</span>
            <span className="flex-1" />
            <Button
              type="button"
              variant="ghost"
              size="icon"
              aria-label="Move earlier"
              disabled={index === 0}
              className="h-7 w-7 disabled:opacity-30"
              onClick={() => move(index, -1)}
            >
              <ArrowUp className="h-3.5 w-3.5" />
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="icon"
              aria-label="Move later"
              disabled={index === value.length - 1}
              className="h-7 w-7 disabled:opacity-30"
              onClick={() => move(index, 1)}
            >
              <ArrowDown className="h-3.5 w-3.5" />
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className="h-7 gap-1.5 px-2 text-xs text-error hover:text-error"
              onClick={() => remove(index)}
            >
              <Trash2 className="h-3.5 w-3.5" />
              Remove
            </Button>
          </div>

          <div className="flex flex-col gap-3.5 p-4">
            <div className="flex flex-wrap items-end gap-3">
              <div className="flex flex-col gap-1.5">
                <Label className="text-xs font-semibold">Method</Label>
                <Select value={action.method} onValueChange={(v) => update(index, { method: v })}>
                  <SelectTrigger className="h-9 w-28">
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
              </div>
              <div className="flex min-w-[220px] flex-1 flex-col gap-1.5">
                <Label className="text-xs font-semibold">Endpoint</Label>
                <Input
                  className="h-9 font-mono text-xs"
                  placeholder="https://api.vendor.com/v1/events"
                  value={action.url}
                  onChange={(e) => update(index, { url: e.target.value })}
                />
              </div>
              <div className="flex flex-col gap-1.5">
                <Label className="text-xs font-semibold">Timeout</Label>
                <Select
                  value={String(action.timeoutSeconds)}
                  onValueChange={(v) => update(index, { timeoutSeconds: Number(v) })}
                >
                  <SelectTrigger className="h-9 w-28">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {TIMEOUT_OPTIONS.map((seconds) => (
                      <SelectItem key={seconds} value={String(seconds)}>
                        {seconds} s
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            </div>

            <div className="flex flex-col gap-2">
              <div className="flex items-center justify-between gap-3">
                <Label className="text-xs font-semibold">Headers</Label>
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  className="h-8 gap-1 px-2 text-xs text-primary hover:text-primary"
                  onClick={() => updateHeader(index, "", "")}
                >
                  <Plus className="h-3.5 w-3.5" />
                  Add header
                </Button>
              </div>
              {Object.entries(action.headers).length === 0 && (
                <p className="text-xs text-medium-emphasis">
                  No headers. <code className="font-mono">Content-Type: application/json</code> is
                  sent by default.
                </p>
              )}
              {Object.entries(action.headers).map(([key, headerValue]) => (
                <div key={key} className="flex flex-wrap items-center gap-2">
                  <Input
                    aria-label="Header name"
                    placeholder="Header name"
                    className="h-9 min-w-[140px] flex-[0_1_190px] font-mono text-xs"
                    value={key}
                    onChange={(e) => renameHeader(index, key, e.target.value)}
                  />
                  <Input
                    aria-label="Header value"
                    placeholder="Value"
                    className="h-9 min-w-[180px] flex-1 font-mono text-xs"
                    value={headerValue}
                    onChange={(e) => updateHeader(index, key, e.target.value)}
                  />
                  <SecretPickerPopover
                    onInsert={(placeholder) =>
                      updateHeader(index, key, `${headerValue}${placeholder}`)
                    }
                  />
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    aria-label="Remove header"
                    className="h-8 w-8 shrink-0 text-medium-emphasis hover:text-error"
                    onClick={() => removeHeader(index, key)}
                  >
                    <Trash2 className="h-4 w-4" />
                  </Button>
                </div>
              ))}
            </div>

            <div className="flex flex-col gap-2">
              <Label className="text-xs font-semibold" id={`fn-body-mode-${action.id}`}>
                Body
              </Label>
              <div
                className="flex flex-wrap gap-2"
                role="radiogroup"
                aria-labelledby={`fn-body-mode-${action.id}`}
              >
                {[
                  { label: "Function result", isTemplate: false },
                  { label: "Template", isTemplate: true },
                ].map((option) => {
                  const isSelected = (action.bodyTemplate != null) === option.isTemplate;
                  return (
                    <button
                      key={option.label}
                      type="button"
                      role="radio"
                      aria-checked={isSelected}
                      className={cn(
                        "rounded-md border px-3 py-1.5 text-xs font-semibold transition-colors",
                        isSelected
                          ? "border-primary bg-blocks-primary-25 text-primary"
                          : "border-border text-medium-emphasis hover:bg-surface-app",
                      )}
                      onClick={() =>
                        update(index, { bodyTemplate: option.isTemplate ? "{{result}}" : null })
                      }
                    >
                      {option.label}
                    </button>
                  );
                })}
              </div>
              {action.bodyTemplate == null ? (
                <p className="text-xs text-medium-emphasis">
                  The returned value is sent as the JSON body, unchanged.
                </p>
              ) : (
                <>
                  <Textarea
                    aria-label="Body template"
                    className="min-h-[90px] resize-y font-mono text-xs"
                    placeholder={'{ "payload": {{result}}, "runId": "{{run.id}}" }'}
                    value={action.bodyTemplate}
                    onChange={(e) => update(index, { bodyTemplate: e.target.value })}
                  />
                  <div className="flex items-center justify-between gap-3">
                    <p className="text-xs text-medium-emphasis">
                      <code className="font-mono">{"{{result}}"}</code> and{" "}
                      <code className="font-mono">{"{{run.id}}"}</code> are substituted before the
                      call.
                    </p>
                    <SecretPickerPopover
                      onInsert={(placeholder) =>
                        update(index, {
                          bodyTemplate: `${action.bodyTemplate ?? ""}${placeholder}`,
                        })
                      }
                    />
                  </div>
                </>
              )}
            </div>
          </div>
        </Card>
      ))}

      {value.length > 0 && (
        <p className="text-xs text-medium-emphasis">
          Secrets are resolved here, never inside the function — a header or body carries{" "}
          <code className="font-mono">{"{{secret.NAME}}"}</code> and the value is substituted on the
          host.
        </p>
      )}
    </div>
  );
};
