import { Tabs, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";

type SegmentedOption<T extends string> = { value: T; label: string };

type SegmentedControlProps<T extends string> = {
  value: T;
  onChange: (value: T) => void;
  options: SegmentedOption<T>[];
  ariaLabel: string;
};

/** A small pick-one switch (two or three words side by side), built on the shared tabs. */
export const SegmentedControl = <T extends string>({
  value,
  onChange,
  options,
  ariaLabel,
}: Readonly<SegmentedControlProps<T>>) => (
  <Tabs
    value={value}
    onValueChange={(next) => onChange(next as T)}
    className="w-auto max-w-full flex-shrink-0"
  >
    <TabsList className="h-8 max-w-full rounded-md bg-muted p-0.5" aria-label={ariaLabel}>
      {options.map((option) => (
        <TabsTrigger key={option.value} value={option.value} className="rounded px-3 text-xs">
          {option.label}
        </TabsTrigger>
      ))}
    </TabsList>
  </Tabs>
);
