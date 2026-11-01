"use client";
import { forwardRef, useMemo, useState } from "react";
import { Check, ChevronsUpDown } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/components/ui-kits/command/command";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui-kits/popover/popover";
import { cn } from "@/lib/utils";
import { browserTimeZone, listTimeZones, offsetLabel, UTC } from "../../utils/timezone";
import { useT } from "@seliseblocks/genesis-os";

interface TimezoneSelectProps {
  value: string;
  onChange: (timeZone: string) => void;
  id?: string;
  disabled?: boolean;
}

type ZoneOption = { id: string; offset: string };

/** Case-insensitive substring match on the item value (which always ends with the zone id). */
export const matchZone = (value: string, search: string): number =>
  value.toLowerCase().includes(search.trim().toLowerCase()) ? 1 : 0;

const ZoneItem = ({
  option,
  value,
  selected,
  label,
  onSelect,
}: Readonly<{ option: ZoneOption; value: string; selected: boolean; label?: string; onSelect: (id: string) => void }>) => (
  <CommandItem value={value} onSelect={() => onSelect(option.id)} className="gap-2">
    <Check className={cn("h-4 w-4 shrink-0", selected ? "opacity-100" : "opacity-0")} aria-hidden />
    <span className="min-w-0 flex-1 truncate">{label ?? option.id}</span>
    <span className="text-xs text-muted-foreground">{option.offset}</span>
  </CommandItem>
);

/** Searchable IANA time zone picker: UTC first, a "Use my timezone" shortcut, then every zone with its offset. */
export const TimezoneSelect = forwardRef<HTMLButtonElement, Readonly<TimezoneSelectProps>>(
  ({ value, onChange, id, disabled }, ref) => {
  const { t } = useT("logic.scheduler");
    const [open, setOpen] = useState(false);
    // Built once: 400+ Intl lookups are too slow to redo on every keystroke.
    const zones = useMemo(() => listTimeZones().map((zone) => ({ id: zone, offset: offsetLabel(zone) })), []);
    const browserZone = useMemo(() => browserTimeZone(), []);
    const utcOption = zones[0];
    const browserOption = browserZone === UTC ? undefined : zones.find((zone) => zone.id === browserZone);

    const select = (zone: string) => {
      onChange(zone);
      setOpen(false);
    };

    return (
      <Popover open={open} onOpenChange={setOpen}>
        <PopoverTrigger asChild>
          <Button
            ref={ref}
            id={id}
            type="button"
            variant="outline"
            role="combobox"
            aria-label={t("TIMEZONE_SELECT.TIMEZONE")}
            aria-expanded={open}
            disabled={disabled}
            className="w-full justify-between font-normal"
          >
            <span className="truncate">
              {value} ({offsetLabel(value)})
            </span>
            <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 text-muted-foreground" aria-hidden />
          </Button>
        </PopoverTrigger>
        <PopoverContent className="w-[--radix-popover-trigger-width] p-0" align="start">
          <Command filter={matchZone}>
            <CommandInput placeholder={t("TIMEZONE_SELECT.SEARCH_TIMEZONE")} />
            <CommandList className="max-h-72">
              <CommandEmpty>{t("TIMEZONE_SELECT.NO_TIMEZONE_FOUND")}</CommandEmpty>
              <CommandGroup heading="UTC">
                <ZoneItem option={utcOption} value={`pinned ${UTC}`} selected={value === UTC} onSelect={select} />
              </CommandGroup>
              {browserOption && (
                <CommandGroup heading={t("TIMEZONE_SELECT.YOUR_TIMEZONE")}>
                  <ZoneItem
                    option={browserOption}
                    value={`browser ${browserOption.id}`}
                    label={t("TIMEZONE_SELECT.USE_MY_TIMEZONE", { id: browserOption.id })}
                    selected={false}
                    onSelect={select}
                  />
                </CommandGroup>
              )}
              <CommandGroup heading={t("TIMEZONE_SELECT.ALL_TIMEZONES")}>
                {zones.map((zone) => (
                  <ZoneItem key={zone.id} option={zone} value={zone.id} selected={value === zone.id} onSelect={select} />
                ))}
              </CommandGroup>
            </CommandList>
          </Command>
        </PopoverContent>
      </Popover>
    );
  },
);
TimezoneSelect.displayName = "TimezoneSelect";
