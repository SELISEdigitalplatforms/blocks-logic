import { cn } from "@/lib/utils";

type IconProps = {
  className?: string;
};

/**
 * "Add child field" glyph — an indent / sub-level elbow arrow with a plus badge in the
 * lower-right. Sized and coloured by `className` so it sits alongside the lucide icons in a row.
 */
export const AddChildIcon = ({ className }: IconProps) => (
  <svg
    xmlns="http://www.w3.org/2000/svg"
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    strokeWidth={2}
    strokeLinecap="round"
    strokeLinejoin="round"
    aria-hidden="true"
    className={cn("h-4 w-4", className)}
  >
    <path d="M4 4v9a2 2 0 0 0 2 2h7" />
    <path d="M10 12l3 3-3 3" />
    <circle cx="18" cy="15" r="5.25" strokeWidth={1.85} />
    <path d="M18 12.5v5M15.5 15h5" strokeWidth={1.85} />
  </svg>
);
