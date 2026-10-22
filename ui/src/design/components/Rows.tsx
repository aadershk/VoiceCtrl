import { useLayoutEffect, useRef } from "react";
import type { ReactNode } from "react";
import { cx } from "../cx";
import { readyHint } from "../hotkeyCopy";
import { nudge, popoverEnter } from "../motion";
import type { IconName } from "../icons";
import { Button, IconButton } from "./Button";
import { Keys, Spinner } from "./Chips";
import { Icon } from "./Icon";

export interface ListRowProps {
  primary: ReactNode;
  /** "time · app", 13 `--ink-3`. */
  meta?: ReactNode;
  /** A leading element such as a trigger chip. */
  lead?: ReactNode;
  /** Icon buttons; they fade in on hover or focus. */
  actions?: ReactNode;
  /** Stable key for FLIP list motion (§10). */
  flipKey?: string;
}

/** List row (§8): one line with ellipsis over the meta line, actions on hover or focus. */
export function ListRow({ primary, meta, lead, actions, flipKey }: ListRowProps) {
  return (
    <div className="vc-row" data-flip-key={flipKey}>
      {lead}
      <div className="vc-row-text">
        <span className="vc-row-primary">{primary}</span>
        {meta && <span className="vc-meta">{meta}</span>}
      </div>
      {actions && <span className="vc-row-actions">{actions}</span>}
    </div>
  );
}

export interface SettingsRowProps {
  label: string;
  description?: ReactNode;
  children: ReactNode;
  /** Quick Find landed here: flash `--tint` for 1.6 s. */
  flash?: boolean;
  id?: string;
}

/** Settings row (§8): label 15/500 over a 13 `--ink-3` description, the control on the right. */
export function SettingsRow({ label, description, children, flash = false, id }: SettingsRowProps) {
  return (
    <div className={cx("vc-setting", flash && "vc-setting-flash")} id={id}>
      <div className="vc-setting-label">
        <b>{label}</b>
        {description && <span>{description}</span>}
      </div>
      <div className="vc-setting-control">{children}</div>
    </div>
  );
}

export interface LatestHeroProps {
  text: string;
  /** Where it went and when, e.g. "into Outlook · 2 min ago". */
  context: string;
  pasteKeys: readonly string[];
  copied?: boolean;
  onPasteAgain: () => void;
  onCopy: () => void;
  /** Smart cleanup edited it, so the original is offered back. */
  onRestoreOriginal?: (() => void) | undefined;
}

/** Latest hero (§8): the one serif moment, Paste Again (primary) + Copy + the key-chip hint. */
export function LatestHero({ text, context, pasteKeys, copied = false, onPasteAgain, onCopy, onRestoreOriginal }: LatestHeroProps) {
  return (
    <section className="vc-card vc-hero" aria-label="Latest Transcription">
      <div className="vc-hero-meta">
        <b>Latest</b> · {context}
      </div>
      <p className="vc-serif vc-hero-text">“{text}”</p>
      <div className="vc-hero-actions">
        <Button variant="primary" icon="paste" onClick={onPasteAgain}>
          Paste Again
        </Button>
        <Button icon={copied ? "check" : "copy"} onClick={onCopy}>
          {copied ? "Copied" : "Copy"}
        </Button>
        {onRestoreOriginal && (
          <Button variant="ghost" icon="retry" onClick={onRestoreOriginal}>
            Restore original
          </Button>
        )}
        <span className="vc-hero-hint">
          or press <Keys keys={pasteKeys} size="sm" />
        </span>
      </div>
    </section>
  );
}

export interface FailedNoticeProps {
  title: string;
  detail: string;
  /** A retry is running: the actions give way to a spinner. */
  busy?: boolean;
  /** Only offered when an offline model is installed (§8). */
  onRetryOffline?: (() => void) | undefined;
  onRetry: () => void;
  onDismiss: () => void;
  /** Words for the two retry buttons; a cancelled recording says "Recover text". */
  retryLabel?: string;
  retryOfflineLabel?: string;
}

/** Failed Dictation notice (§8): warn tokens, alert icon; enters with the nudge, the one overshoot. */
export function FailedNotice({
  title,
  detail,
  busy = false,
  onRetryOffline,
  onRetry,
  onDismiss,
  retryLabel = "Retry",
  retryOfflineLabel = "Retry offline",
}: FailedNoticeProps) {
  const ref = useRef<HTMLElement>(null);
  useLayoutEffect(() => {
    if (ref.current) nudge(ref.current);
  }, []);
  return (
    <section ref={ref} className="vc-notice" role="alert">
      <Icon name="alert" />
      <div className="vc-notice-text">
        <span className="vc-notice-title">{title}</span>
        <span className="vc-notice-detail">{detail}</span>
      </div>
      {busy ? (
        <span className="vc-notice-busy">
          <Spinner />
          Transcribing
        </span>
      ) : (
        <>
          {onRetryOffline && (
            <Button variant="ghost" onClick={onRetryOffline}>
              {retryOfflineLabel}
            </Button>
          )}
          <Button icon="retry" onClick={onRetry}>
            {retryLabel}
          </Button>
          <IconButton icon="close" label="Dismiss and drop the audio" onClick={onDismiss} />
        </>
      )}
    </section>
  );
}

export interface InlineEditorProps {
  children: ReactNode;
  saveLabel: string;
  onSave: () => void;
  onCancel: () => void;
  /** A button on the left of the actions, e.g. Delete in an editor for something that exists. */
  leading?: ReactNode;
}

/** Inline editor (§8): a `--tint` card that replaces the row in place, Esc cancels. */
export function InlineEditor({ children, saveLabel, onSave, onCancel, leading }: InlineEditorProps) {
  const ref = useRef<HTMLFormElement>(null);
  useLayoutEffect(() => {
    if (ref.current) popoverEnter(ref.current, "top center");
  }, []);
  return (
    <form
      ref={ref}
      className="vc-editor"
      onSubmit={(event) => {
        event.preventDefault();
        onSave();
      }}
      onKeyDown={(event) => {
        if (event.key === "Escape") onCancel();
      }}
    >
      {children}
      <div className="vc-editor-actions">
        {leading}
        {leading && <span className="vc-editor-spacer" />}
        <Button variant="ghost" onClick={onCancel}>
          Cancel
        </Button>
        <Button variant="primary" type="submit">
          {saveLabel}
        </Button>
      </div>
    </form>
  );
}

export type StatusCardState =
  | { kind: "ready"; hotkey: string; talkKeys?: readonly string[] | null }
  | {
      kind: "warning";
      icon: IconName;
      title: string;
      fix: string;
      onFix: () => void;
      /** `neutral` for a state the user chose (Paused): ink, not the warn colours. */
      tone?: "warn" | "neutral";
    };

export interface StatusCardProps {
  state: StatusCardState;
  theme: "light" | "dark";
  onToggleTheme: () => void;
}

/** Status card (§8): sidebar foot on `--tint`; "Ready" or a warning that links to the fix. */
export function StatusCard({ state, theme, onToggleTheme }: StatusCardProps) {
  return (
    <div className="vc-status">
      {state.kind === "ready" ? (
        <div className="vc-status-text">
          <span className="vc-status-title">Ready</span>
          <span className="vc-status-hint">{readyHint(state.hotkey, state.talkKeys)}</span>
        </div>
      ) : (
        <button
          type="button"
          className={cx("vc-status-warning", state.tone === "neutral" && "vc-status-neutral")}
          onClick={state.onFix}
        >
          <Icon name={state.icon} size={16} />
          <span className="vc-status-text">
            <span className="vc-status-title">{state.title}</span>
            <span className="vc-status-hint">{state.fix}</span>
          </span>
        </button>
      )}
      <IconButton
        icon={theme === "light" ? "moon" : "sun"}
        label={theme === "light" ? "Switch to Dark" : "Switch to Light"}
        onClick={onToggleTheme}
      />
    </div>
  );
}

export interface EmptyStateProps {
  art: ReactNode;
  title: string;
  children: ReactNode;
  actions?: ReactNode;
}

/** Empty and error states: art tile, Sora 20 title, a short line and at most two actions. */
export function EmptyState({ art, title, children, actions }: EmptyStateProps) {
  return (
    <section className="vc-empty">
      {art}
      <h2 className="vc-dialog-title">{title}</h2>
      <p className="vc-sub">{children}</p>
      {actions && <div className="vc-empty-actions">{actions}</div>}
    </section>
  );
}
