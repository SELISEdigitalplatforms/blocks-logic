import type { ReactNode } from "react";

type GuideContentProps = {
  title: string;
  description: ReactNode;
  steps: ReactNode[];
  notes?: ReactNode[];
};

export const GuideContent = ({ title, description, steps, notes }: GuideContentProps) => (
  <div className="space-y-5 text-sm leading-6 text-muted-foreground">
    <section className="space-y-2">
      <h4 className="text-sm font-semibold text-foreground">{title}</h4>
      <p>{description}</p>
    </section>

    <section className="space-y-2">
      <h5 className="text-xs font-semibold uppercase tracking-wide text-foreground">How to use</h5>
      <ol className="list-decimal space-y-2 pl-5">
        {steps.map((step, index) => (
          <li key={index}>{step}</li>
        ))}
      </ol>
    </section>

    {notes?.length ? (
      <section className="space-y-2 rounded-md border border-border bg-muted/30 p-3">
        <h5 className="text-xs font-semibold uppercase tracking-wide text-foreground">
          Important notes
        </h5>
        <ul className="list-disc space-y-1.5 pl-5">
          {notes.map((note, index) => (
            <li key={index}>{note}</li>
          ))}
        </ul>
      </section>
    ) : null}
  </div>
);
