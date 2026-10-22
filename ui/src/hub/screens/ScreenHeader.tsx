import type { ReactNode } from "react";

export interface ScreenHeaderProps {
  title: string;
  subtitle: string;
  /** The header's actions, on the right (research 10 "Screens"). */
  actions?: ReactNode;
}

/** Every screen's header: a Sora 30 title over a one-line `--ink-2` subtitle, actions on the right. */
export function ScreenHeader({ title, subtitle, actions }: ScreenHeaderProps) {
  return (
    <header className="hub-header">
      <div className="hub-header-text">
        <h1 className="vc-title">{title}</h1>
        <p className="vc-sub">{subtitle}</p>
      </div>
      {actions && <div className="hub-header-actions">{actions}</div>}
    </header>
  );
}
