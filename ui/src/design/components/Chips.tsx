import type { ReactNode } from "react";
import { cx } from "../cx";

/** Key chip (§8): 26 h, 1 px `--line-2` with a 2 px bottom border. `sm` is the hero's 22 h hint. */
export function KeyChip({ children, size = "md" }: { children: ReactNode; size?: "md" | "sm" }) {
  return <kbd className={cx("vc-key", size === "sm" && "vc-key-sm")}>{children}</kbd>;
}

/** A chord such as Ctrl + Shift + V, one chip per key. */
export function Keys({ keys, size = "md" }: { keys: readonly string[]; size?: "md" | "sm" }) {
  return (
    <span className="vc-keys" aria-label={keys.join(" + ")}>
      {keys.map((key, index) => (
        <KeyChip key={`${key}-${index}`} size={size}>
          {key}
        </KeyChip>
      ))}
    </span>
  );
}

/** Trigger chip (Snippets, §8): 28 h on `--tint`, 14/600. */
export function TriggerChip({ children }: { children: ReactNode }) {
  return <span className="vc-trigger">{children}</span>;
}

/** Spinner (§8): 16 px ring with an `--ink` arc, 900 ms linear. */
export function Spinner({ label }: { label?: string }) {
  return (
    <span
      className="vc-spinner"
      role={label ? "status" : undefined}
      aria-label={label}
      aria-hidden={label ? undefined : true}
    />
  );
}

export interface ProgressBarProps {
  /** 0 to 1. */
  value: number;
  label: string;
  /** Fixed width in px (140 in the Settings update row); fills its container when omitted. */
  width?: number;
}

/** A 6 px progress bar whose fill scales on X, so it never animates a layout property. */
export function ProgressBar({ value, label, width }: ProgressBarProps) {
  const clamped = Math.min(1, Math.max(0, value));
  return (
    <span
      className="vc-progress"
      role="progressbar"
      aria-label={label}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={Math.round(clamped * 100)}
      style={{ width: width ?? "100%" }}
    >
      <i className="vc-progress-fill" style={{ transform: `scaleX(${clamped})` }} />
    </span>
  );
}
