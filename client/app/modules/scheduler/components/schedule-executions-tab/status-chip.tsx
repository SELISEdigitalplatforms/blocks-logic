import { Badge } from "@/components/ui-kits/badge/badge";

export const StatusChip = ({ label }: Readonly<{ label: string }>) => (
  <Badge variant="outline" className="w-fit font-mono text-xs">
    {label}
  </Badge>
);
