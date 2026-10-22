/**
 * The Overlay's layout rules (DESIGN §9, research 11 "States"), free of the DOM so they can be
 * unit-tested: which layer each phase shows, how wide the pill is, which way it grows on each
 * dock, where its caps and middle go, where the resting handle sits, the words, and what the live
 * region says.
 */
import type { FailureReason, OverlayDock, OverlayNotice, OverlayPhase, OverlayState } from "../bridge/contracts.g";
import type { IconName } from "../design/icons";

/** The window the page fills (C# `OverlayGeometry`): keep these in step with it. */
export const STAGE = { width: 320, height: 96 } as const;

/** The pill's height and its bottom edge's distance from the window's bottom (C# `PillBottomInsetDip`). */
export const PILL_HEIGHT = 32;
export const PILL_BOTTOM = 32;

/** The end caps' width: the logo tile's 11 px radius. */
export const CAP = 11;

/** The pill's centre in stage pixels on the Bottom dock. */
export const PILL_CENTER = { x: STAGE.width / 2, y: STAGE.height - PILL_BOTTOM - PILL_HEIGHT / 2 } as const;

/** On the Left and Right docks the pill sits this far from the window's edge, which is the screen's
 *  edge (C# `OverlayGeometry.SideGapDip`). */
export const SIDE_GAP = 16;

/** The resting handle for "Always show the Overlay" (DESIGN §9): 48 × 8, radius 4, its bottom on
 *  the pill's bottom edge. */
export const HANDLE = { width: 48, height: 8 } as const;

/**
 * How the pill grows on a dock (research 11, Docking): the share of its width that lies left of
 * its anchor. Bottom grows from the centre, Left rightwards from its left edge, Right leftwards
 * from its right edge.
 */
export function growth(dock: OverlayDock): number {
  return dock === "left" ? 0 : dock === "right" ? 1 : 0.5;
}

/** The anchor's x in the stage: the pill's centre, left edge or right edge by dock. */
export function anchorX(dock: OverlayDock): number {
  return dock === "left" ? SIDE_GAP : dock === "right" ? STAGE.width - SIDE_GAP : STAGE.width / 2;
}

export type Layer = "ready" | "listening" | "transcribing" | "pasted" | "noSpeech" | "failed" | "notice";

export const LAYERS: readonly Layer[] = ["ready", "listening", "transcribing", "pasted", "noSpeech", "failed", "notice"];

/** Widths from DESIGN §9; the hover width where the phase reveals controls on hover. Failed's
 *  196 is a minimum: "Service error" needs a few pixels more, and a reason must never be cut. */
export const WIDTHS: Record<Exclude<Layer, "notice">, { rest: number; hover: number }> = {
  ready: { rest: 32, hover: 32 },
  listening: { rest: 54, hover: 152 },
  transcribing: { rest: 56, hover: 88 },
  pasted: { rest: 32, hover: 32 },
  noSpeech: { rest: 112, hover: 112 },
  failed: { rest: 196, hover: 196 },
};

/** Extra width the lock cue gives the Listening pill: its 14 px icon and the layer's 8 px gap. */
export const LOCK_EXTRA = 22;

/** Extra width "Writing email" gives the Transcribing pill while an Email request's email is
 *  written (ticket 57): the words and the layer's 8 px gap. */
export const WRITING_EXTRA = 74;

/** The layer a phase shows; Hidden shows none (the last layer stays while it fades). The handle's
 *  pill is the Ready tile, shown while the pointer is over it. */
export function layerFor(phase: OverlayPhase): Layer | null {
  if (phase === "hidden") return null;
  return phase === "handle" ? "ready" : phase;
}

/** Only Listening and Transcribing grow on hover (their timer and ✕). */
export function revealsOnHover(layer: Layer): boolean {
  return layer === "listening" || layer === "transcribing";
}

/** Layers whose width follows their words, measured by the page. */
export function isMeasured(layer: Layer): layer is "notice" | "failed" {
  return layer === "notice" || layer === "failed";
}

/** The pill's width for a layer. `measured` is the layer's natural width, for measured layers. */
export function pillWidth(layer: Layer, hovered: boolean, measured: number, locked = false, writing = false): number {
  if (layer === "notice") return measured;
  if (layer === "failed") return Math.max(WIDTHS.failed.rest, measured);
  const width = WIDTHS[layer];
  const base = hovered && revealsOnHover(layer) ? width.hover : width.rest;
  if (layer === "listening" && locked) return base + LOCK_EXTRA;
  return layer === "transcribing" && writing ? base + WRITING_EXTRA : base;
}

/**
 * The pill as parts (research 15, rule 3): two caps and a middle scaled on X, all positioned
 * relative to the pill's anchor. The middle is 100 px wide at scale 1 and overlaps each cap by
 * 1 px, so no seam shows between them.
 */
export function pillParts(width: number, dock: OverlayDock = "bottom"): { left: number; midX: number; midScale: number; rightX: number } {
  const left = -width * growth(dock);
  return { left, midX: left + CAP - 1, midScale: Math.max(0, width - 2 * (CAP - 1)) / 100, rightX: left + width - CAP };
}

export interface Rect {
  x: number;
  y: number;
  width: number;
  height: number;
}

/** The pill's rectangle in stage pixels, as reported to the host for hit-testing. */
export function pillRect(width: number, dock: OverlayDock = "bottom"): Rect {
  return { x: anchorX(dock) - width * growth(dock), y: PILL_CENTER.y - PILL_HEIGHT / 2, width, height: PILL_HEIGHT };
}

/** The resting handle's rectangle in stage pixels: on the pill's bottom edge, placed by the same
 *  growth rule as the pill. */
export function handleRect(dock: OverlayDock = "bottom"): Rect {
  return {
    x: anchorX(dock) - HANDLE.width * growth(dock),
    y: PILL_CENTER.y + PILL_HEIGHT / 2 - HANDLE.height,
    width: HANDLE.width,
    height: HANDLE.height,
  };
}

/**
 * What the host hit-tests while the handle rests. Under the pointer it covers both the handle and
 * the Ready tile it turns into, so the pointer can't fall off the edge of one onto the other and
 * make the two flicker.
 */
export function handleHitRect(dock: OverlayDock, hovered: boolean): Rect {
  const handle = handleRect(dock);
  if (!hovered) return handle;
  const tile = pillRect(WIDTHS.ready.rest, dock);
  const x = Math.min(handle.x, tile.x);
  const y = Math.min(handle.y, tile.y);
  return {
    x,
    y,
    width: Math.max(handle.x + handle.width, tile.x + tile.width) - x,
    height: Math.max(handle.y + handle.height, tile.y + tile.height) - y,
  };
}

export const FAILURE_TEXT: Record<FailureReason, string> = {
  noInternet: "No internet",
  timedOut: "Timed out",
  serviceError: "Service error",
  cancelled: "Cancelled",
};

export interface NoticeCopy {
  /** Null for the two notices that sit over a running transcription: words and Cancel only. */
  icon: IconName | null;
  text: string;
  /** "Add an API key" is a button: it opens Settings at the key row. */
  opensKeySettings: boolean;
  /** "Still transcribing", "Still writing" and "Taking longer than usual" end in a Cancel button. */
  hasCancel?: boolean;
  /** "Cancelled" ends in an Undo button: the recording was kept and Undo transcribes it. */
  hasUndo?: boolean;
}

/** The "icon + few words" messages (DESIGN §9, §12). */
export const NOTICES: Record<OverlayNotice, NoticeCopy> = {
  micUnavailable: { icon: "overlayMicOff", text: "Mic unavailable", opensKeySettings: false },
  addApiKey: { icon: "key", text: "Add an API key", opensKeySettings: true },
  copiedPressCtrlV: { icon: "copy", text: "Copied · press Ctrl+V", opensKeySettings: false },
  pasteFailed: { icon: "overlayAlert", text: "Paste failed", opensKeySettings: false },
  copyFailed: { icon: "overlayAlert", text: "Copy failed · Try again", opensKeySettings: false },
  nothingToRewrite: { icon: "overlayAlert", text: "Select text to rewrite", opensKeySettings: false },
  rewriteNeedsLanguageModel: { icon: "overlayAlert", text: "Turn on Smart cleanup to rewrite offline", opensKeySettings: false },
  rewriteFailed: { icon: "overlayAlert", text: "Couldn't rewrite · Text unchanged", opensKeySettings: false },
  emailNotWritten: { icon: "overlayAlert", text: "Couldn't write the email · Try again", opensKeySettings: false },
  selectTextboxFirst: { icon: "overlayAlert", text: "Select a textbox first · Copied", opensKeySettings: false },
  noMicAudio: { icon: "overlayMicOff", text: "No audio from the mic · Is it muted?", opensKeySettings: false },
  noWordsHeard: { icon: "overlayMicOff", text: "No words heard · Try again", opensKeySettings: false },
  micStopped: { icon: "overlayMicOff", text: "Mic stopped · Nothing pasted", opensKeySettings: false },
  stillTranscribing: { icon: null, text: "Still transcribing", opensKeySettings: false, hasCancel: true },
  stillWriting: { icon: null, text: "Still writing", opensKeySettings: false, hasCancel: true },
  takingLonger: { icon: null, text: "Taking longer than usual", opensKeySettings: false, hasCancel: true },
  cancelledUndo: { icon: null, text: "Cancelled", opensKeySettings: false, hasUndo: true },
  recordingLimitSoon: { icon: "overlayAlert", text: "Under a minute left to record", opensKeySettings: false },
  recordingLimitReached: { icon: "overlayAlert", text: "20 minute limit reached · Pasted", opensKeySettings: false },
};

/** The words of a notice. "Paste failed" names the Paste Again key when the host sent it, so the
 *  way to recover is on the pill. */
export function noticeText(notice: OverlayNotice, key: string | null): string {
  const text = NOTICES[notice].text;
  return notice === "pasteFailed" && key ? `${text} · ${key} to paste again` : text;
}

/** What the polite live region says for a state (research 11 "Interaction"). */
export function liveText(state: OverlayState): string {
  switch (state.phase) {
    case "hidden":
    case "handle":
      return "";
    case "ready":
      return "Ready. Click to dictate.";
    case "listening":
      if (state.locked && !state.rewrite) return "Listening, locked on. Press the keys again to finish.";
      return state.rewrite ? "Listening for your edit" : "Listening";
    case "transcribing":
      if (state.writing) return "Writing your email";
      return state.rewrite ? "Rewriting" : "Transcribing";
    case "pasted":
      return "Pasted";
    case "noSpeech":
      return "No speech";
    case "failed":
      return `${FAILURE_TEXT[state.failureReason ?? "serviceError"]}. Retry available.`;
    case "notice":
      return state.notice ? noticeText(state.notice, state.noticeKey) : "";
  }
}

/** The hover timer: m:ss since recording began. */
export function formatElapsed(ms: number): string {
  const seconds = Math.max(0, Math.floor(ms / 1000));
  const s = seconds % 60;
  return `${Math.floor(seconds / 60)}:${s < 10 ? "0" : ""}${s}`;
}

/** Each bar's share of the level, so the three move a little differently (the logo's rhythm). */
const BAR_SHARE = [0.7, 1, 0.85] as const;

/** A bar never shrinks below this (the bars stay readable as the logo's). */
export const BAR_FLOOR = 0.25;

/** The three live bars' `scaleY` for a mic level of 0..1. */
export function barScales(level: number): [number, number, number] {
  const clamped = Math.min(1, Math.max(0, Number.isFinite(level) ? level : 0));
  const scale = (share: number) => BAR_FLOOR + (1 - BAR_FLOOR) * Math.min(1, clamped * share * 1.4);
  return [scale(BAR_SHARE[0]), scale(BAR_SHARE[1]), scale(BAR_SHARE[2])];
}

/** One frame of level smoothing: moves part of the way to the target each frame. */
export function smooth(current: number, target: number): number {
  return current + (target - current) * 0.35;
}
