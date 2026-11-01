import type { ReactNode } from "react";
import { useT } from "@seliseblocks/genesis-os";

type GuideSection = {
  title: string;
  content: ReactNode;
};

type GuideContentProps = {
  title: string;
  description: ReactNode;
  steps: ReactNode[];
  sections?: GuideSection[];
  notes?: ReactNode[];
};

export const GuideInlineCode = ({ children }: { children: ReactNode }) => (
  <code className="rounded bg-muted px-1 py-0.5 font-mono text-xs text-foreground">{children}</code>
);

export const GuideCode = ({ children }: { children: string }) => (
  <pre className="overflow-x-auto rounded-md border border-border bg-muted/40 p-3 font-mono text-xs leading-5 text-foreground">
    <code>{children}</code>
  </pre>
);

export const GuideContent = ({ title, description, steps, sections, notes }: GuideContentProps) => {
  const { t } = useT("logic.workflow.nodes.guides");
  return (
    <div className="space-y-5 text-sm leading-6 text-muted-foreground">
      <section className="space-y-2">
        <h4 className="text-sm font-semibold text-foreground">{title}</h4>
        <p>{description}</p>
      </section>

      <section className="space-y-2">
        <h5 className="text-xs font-semibold uppercase tracking-wide text-foreground">
          {t("NODE_GUIDE_CONTENT.HOW_TO_USE")}
        </h5>
        <ol className="list-decimal space-y-2 pl-5">
          {steps.map((step, index) => (
            <li key={index} /* NOSONAR: static ReactNode list */>{step}</li>
          ))}
        </ol>
      </section>

      {sections?.map((section) => (
        <section key={section.title} className="space-y-2">
          <h5 className="text-xs font-semibold uppercase tracking-wide text-foreground">
            {section.title}
          </h5>
          {section.content}
        </section>
      ))}

      {notes?.length ? (
        <section className="space-y-2 rounded-md border border-border bg-muted/30 p-3">
          <h5 className="text-xs font-semibold uppercase tracking-wide text-foreground">
            {t("NODE_GUIDE_CONTENT.IMPORTANT_NOTES")}
          </h5>
          <ul className="list-disc space-y-1.5 pl-5">
            {notes.map((note, index) => (
              <li key={index} /* NOSONAR: static ReactNode list */>{note}</li>
            ))}
          </ul>
        </section>
      ) : null}
    </div>
  );
};
