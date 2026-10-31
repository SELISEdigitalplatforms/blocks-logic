import { ReactNode } from "react";

interface ExecutionSectionProps {
  title: string;
  children: ReactNode;
}

/** One labelled block of the execution sheet. */
export const ExecutionSection = ({ title, children }: Readonly<ExecutionSectionProps>) => (
  <section aria-label={title} className="space-y-3">
    <h3 className="text-xs font-medium uppercase text-muted-foreground">{title}</h3>
    {children}
  </section>
);

/** A muted one-line note inside a section. */
export const SectionNote = ({ children }: Readonly<{ children: ReactNode }>) => (
  <p className="text-xs text-muted-foreground">{children}</p>
);
