import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { BACKOFF_KIND_OPTIONS } from "../../constants/limits.constant";
import { IRetryPolicy } from "../../types/function.types";

type RetryFormProps = {
  value: IRetryPolicy;
  onChange: (value: IRetryPolicy) => void;
};

export const RetryForm = ({ value, onChange }: RetryFormProps) => {
  const patch = (partial: Partial<IRetryPolicy>) => onChange({ ...value, ...partial });

  return (
    <div className="space-y-5">
      <div className="grid grid-cols-2 gap-4">
        <div className="space-y-1.5">
          <Label>Attempts</Label>
          <Input
            type="number"
            min={1}
            value={value.attempts}
            onChange={(e) => patch({ attempts: Number(e.target.value) })}
          />
          <p className="text-xs text-muted-foreground">Includes the first attempt; 1 means no retry.</p>
        </div>
        <div className="space-y-1.5">
          <Label>Backoff</Label>
          <Select value={value.backoff} onValueChange={(v) => patch({ backoff: v as IRetryPolicy["backoff"] })}>
            <SelectTrigger>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {BACKOFF_KIND_OPTIONS.map((option) => (
                <SelectItem key={option.value} value={option.value}>
                  {option.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </div>

      {value.backoff !== "None" && (
        <div className="grid grid-cols-2 gap-4 border-t pt-4">
          <div className="space-y-1.5">
            <Label>Initial delay (seconds)</Label>
            <Input
              type="number"
              min={1}
              value={value.initialDelaySeconds}
              onChange={(e) => patch({ initialDelaySeconds: Number(e.target.value) })}
            />
          </div>
          <div className="space-y-1.5">
            <Label>Max delay (seconds)</Label>
            <Input
              type="number"
              min={1}
              value={value.maxDelaySeconds}
              onChange={(e) => patch({ maxDelaySeconds: Number(e.target.value) })}
            />
          </div>
        </div>
      )}
    </div>
  );
};
