import type { ReactNode } from "react";

export function Section({ title, children, testId }: { title: string; children: ReactNode; testId?: string }) {
  return (
    <section className="g-section" data-testid={testId}>
      <h2>{title}</h2>
      <div className="g-panel">{children}</div>
    </section>
  );
}
