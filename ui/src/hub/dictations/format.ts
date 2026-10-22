/**
 * The Dictations screen's words and groupings (research 10 "Dictations"). Times come from the host
 * as Unix milliseconds; everything here is local time, formatted in the user's locale.
 */
import type { DictationItem, FailedDictationItem, FailureReason, KeepDictations } from "../../bridge/contracts.g";

const DAY_MS = 24 * 60 * 60 * 1000;

function startOfDay(ms: number): number {
  const d = new Date(ms);
  return new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
}

/** Whole days between two local dates, ignoring the time (safe across DST changes). */
function daysBetween(earlierMs: number, laterMs: number): number {
  return Math.round((startOfDay(laterMs) - startOfDay(earlierMs)) / DAY_MS);
}

export function clock(ms: number, locale?: string): string {
  return new Intl.DateTimeFormat(locale, { hour: "2-digit", minute: "2-digit" }).format(ms);
}

/** "Today", "Yesterday", a weekday within the week, then "14 September". */
export function dayLabel(ms: number, now: number, locale?: string): string {
  const days = daysBetween(ms, now);
  if (days <= 0) return "Today";
  if (days === 1) return "Yesterday";
  if (days < 7) return new Intl.DateTimeFormat(locale, { weekday: "long" }).format(ms);
  return new Intl.DateTimeFormat(locale, { day: "numeric", month: "long" }).format(ms);
}

export interface DayGroup {
  label: string;
  items: DictationItem[];
}

/** Newest-first items into day groups, in order. */
export function dayGroups(items: readonly DictationItem[], now: number, locale?: string): DayGroup[] {
  const groups: DayGroup[] = [];
  for (const item of items) {
    const label = dayLabel(item.atMs, now, locale);
    const last = groups[groups.length - 1];
    if (last?.label === label) last.items.push(item);
    else groups.push({ label, items: [item] });
  }
  return groups;
}

/** How long ago, in the hero's words: "just now", "2 min ago", "today at 07:05". */
export function ago(ms: number, now: number, locale?: string): string {
  const minutes = Math.floor((now - ms) / 60_000);
  if (minutes < 1) return "just now";
  if (minutes < 60) return `${minutes} min ago`;
  return `${dayLabel(ms, now, locale).toLowerCase()} at ${clock(ms, locale)}`;
}

/** The Latest hero's line after "Latest": "into Outlook · 2 min ago". */
export function latestContext(item: DictationItem, now: number, locale?: string): string {
  const when = ago(item.atMs, now, locale);
  return item.app ? `into ${item.app} · ${when}` : when;
}

/** A row's meta line: "09:12 · Notion". */
export function rowMeta(item: DictationItem, locale?: string): string {
  const time = clock(item.atMs, locale);
  return item.app ? `${time} · ${item.app}` : time;
}

export function matches(item: DictationItem, query: string): boolean {
  const q = query.trim().toLocaleLowerCase();
  return q.length === 0 || item.text.toLocaleLowerCase().includes(q);
}

const REASON: Record<FailureReason, string> = {
  noInternet: "no internet",
  timedOut: "it timed out",
  serviceError: "the service had a problem",
  cancelled: "cancelled",
};

/** How long a cancelled recording stays recoverable (C# `DictationController.CancelledRecoveryWindow`). */
export const CANCELLED_WINDOW_MINUTES = 10;

export function failedTitle(failed: FailedDictationItem): string {
  return failed.reason === "cancelled"
    ? "You cancelled a recording: its text can still be recovered"
    : `One Dictation didn't go through: ${REASON[failed.reason]}`;
}

export function failedDetail(failed: FailedDictationItem, locale?: string): string {
  const where = [failed.app, clock(failed.atMs, locale)].filter(Boolean).join(" · ");
  return failed.reason === "cancelled"
    ? `${where} · ${failed.audioSeconds} s of audio, kept for ${CANCELLED_WINDOW_MINUTES} minutes, until you recover it, dismiss it or quit`
    : `${where} · ${failed.audioSeconds} s of audio, kept until you retry, dismiss it or quit`;
}

const SUBTITLE: Record<KeepDictations, string> = {
  last50: "Your last 50, kept on this PC. Audio is never saved.",
  last24Hours: "The last 24 hours, kept on this PC. Audio is never saved.",
  none: "Not kept. Audio is never saved either.",
};

export function subtitleFor(keep: KeepDictations): string {
  return SUBTITLE[keep];
}
