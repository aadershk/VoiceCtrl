import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { OverlayState } from "../src/bridge/contracts.g";
import { barScales, BAR_FLOOR, formatElapsed, liveText, LOCK_EXTRA, noticeText, pillParts, pillRect, pillWidth, WRITING_EXTRA } from "../src/overlay/layout";
import { createOverlay } from "../src/overlay/overlay";
import type { Overlay, OverlayAction } from "../src/overlay/overlay";

function state(partial: Partial<OverlayState> & Pick<OverlayState, "phase">): OverlayState {
  return {
    seq: 1,
    canStart: false,
    showsTimer: false,
    canCancel: false,
    canRetry: false,
    canDismiss: false,
    canFinish: false,
    canUndo: false,
    listeningSinceMs: null,
    failureReason: null,
    failedId: null,
    notice: null,
    noticeKey: null,
    afterCancel: false,
    rewrite: false,
    locked: false,
    writing: false,
    ...partial,
  };
}

describe("Overlay layout (DESIGN §9)", () => {
  it("builds the pill from two 11 px caps and a middle that overlaps each by 1 px", () => {
    expect(pillParts(54)).toEqual({ left: -27, midX: -17, midScale: 0.34, rightX: 16 });
  });

  it("grows Listening and Transcribing on hover only", () => {
    expect(pillWidth("listening", false, 0)).toBe(54);
    expect(pillWidth("listening", true, 0)).toBe(152);
    expect(pillWidth("transcribing", true, 0)).toBe(88);
    expect(pillWidth("failed", true, 0)).toBe(196);
    expect(pillWidth("notice", false, 143)).toBe(143);
  });

  it("makes room for the lock cue on a locked Listening pill only", () => {
    expect(pillWidth("listening", false, 0, true)).toBe(54 + LOCK_EXTRA);
    expect(pillWidth("listening", true, 0, true)).toBe(152 + LOCK_EXTRA);
    expect(pillWidth("transcribing", false, 0, true)).toBe(56);
    expect(liveText(state({ phase: "listening", locked: true }))).toBe("Listening, locked on. Press the keys again to finish.");
  });

  it("says it is writing an email on a wider Transcribing pill only", () => {
    expect(pillWidth("transcribing", false, 0, false, true)).toBe(56 + WRITING_EXTRA);
    expect(pillWidth("transcribing", true, 0, false, true)).toBe(88 + WRITING_EXTRA);
    expect(pillWidth("listening", false, 0, false, true)).toBe(54);
    expect(liveText(state({ phase: "transcribing", writing: true }))).toBe("Writing your email");
    expect(liveText(state({ phase: "notice", notice: "stillWriting" }))).toBe("Still writing");
  });

  it("puts the pill 32 px above the window's bottom, centred", () => {
    expect(pillRect(32)).toEqual({ x: 144, y: 32, width: 32, height: 32 });
  });

  it("says what happened in the live region", () => {
    expect(liveText(state({ phase: "failed", failureReason: "timedOut" }))).toBe("Timed out. Retry available.");
    expect(liveText(state({ phase: "notice", notice: "copiedPressCtrlV" }))).toBe("Copied · press Ctrl+V");
    expect(liveText(state({ phase: "hidden" }))).toBe("");
  });

  it("says a rewrite is a rewrite, and gives its notices their own words", () => {
    expect(liveText(state({ phase: "listening", rewrite: true }))).toBe("Listening for your edit");
    expect(liveText(state({ phase: "transcribing", rewrite: true }))).toBe("Rewriting");
    expect(liveText(state({ phase: "listening" }))).toBe("Listening");
    expect(liveText(state({ phase: "notice", notice: "nothingToRewrite" }))).toBe("Select text to rewrite");
    expect(liveText(state({ phase: "notice", notice: "rewriteFailed" }))).toBe("Couldn't rewrite · Text unchanged");
    expect(liveText(state({ phase: "notice", notice: "rewriteNeedsLanguageModel" }))).toBe("Turn on Smart cleanup to rewrite offline");
  });

  it("gives each nothing-heard notice its own words", () => {
    expect(liveText(state({ phase: "notice", notice: "noMicAudio" }))).toBe("No audio from the mic · Is it muted?");
    expect(liveText(state({ phase: "notice", notice: "noWordsHeard" }))).toBe("No words heard · Try again");
    expect(liveText(state({ phase: "notice", notice: "micStopped" }))).toBe("Mic stopped · Nothing pasted");
    expect(liveText(state({ phase: "notice", notice: "takingLonger" }))).toBe("Taking longer than usual");
    expect(liveText(state({ phase: "notice", notice: "stillTranscribing" }))).toBe("Still transcribing");
    expect(liveText(state({ phase: "notice", notice: "selectTextboxFirst" }))).toBe("Select a textbox first · Copied");
  });

  it("names the Paste Again key in Paste failed, when the host sends one", () => {
    expect(noticeText("pasteFailed", "Ctrl+Shift+V")).toBe("Paste failed · Ctrl+Shift+V to paste again");
    expect(noticeText("pasteFailed", null)).toBe("Paste failed");
    expect(noticeText("copyFailed", "Ctrl+Shift+V")).toBe("Copy failed · Try again");
    expect(liveText(state({ phase: "notice", notice: "pasteFailed", noticeKey: "Ctrl+Alt+P" }))).toBe(
      "Paste failed · Ctrl+Alt+P to paste again",
    );
  });

  it("formats the hover timer as m:ss", () => {
    expect(formatElapsed(67_400)).toBe("1:07");
    expect(formatElapsed(-5)).toBe("0:00");
  });

  it("keeps the level bars between their floor and full height", () => {
    expect(barScales(0)).toEqual([BAR_FLOOR, BAR_FLOOR, BAR_FLOOR]);
    expect(Math.max(...barScales(5))).toBe(1);
    expect(barScales(Number.NaN)).toEqual([BAR_FLOOR, BAR_FLOOR, BAR_FLOOR]);
  });
});

describe("Overlay page", () => {
  let stage: HTMLElement;
  let overlay: Overlay;
  let acts: OverlayAction[];
  let rects: { x: number; y: number; width: number; height: number }[];

  beforeEach(() => {
    stage = document.createElement("div");
    document.body.append(stage);
    acts = [];
    rects = [];
    overlay = createOverlay(stage, {
      act: (action) => acts.push(action),
      reportHitRect: (rect) => rects.push(rect),
      painted: () => undefined,
    });
  });

  afterEach(() => {
    overlay.dispose();
    stage.remove();
    vi.restoreAllMocks();
  });

  /** jsdom does no layout, so hit-testing answers with the element the test names. */
  function pointAt(selector: string | null) {
    document.elementFromPoint = () => (selector ? stage.querySelector(selector) : null);
  }

  it("reports the pill's rectangle on show and a zero one when hidden", () => {
    overlay.render(state({ phase: "ready", canStart: true }));
    overlay.render(state({ seq: 2, phase: "hidden" }));

    expect(rects).toEqual([
      { x: 144, y: 32, width: 32, height: 32 },
      { x: 0, y: 0, width: 0, height: 0 },
    ]);
  });

  it("starts a Dictation when the Ready tile is pressed and released", () => {
    overlay.render(state({ phase: "ready", canStart: true }));
    pointAt(".ov-tile");

    overlay.pointer("enter", 160, 48);
    overlay.pointer("down", 160, 48);
    overlay.pointer("up", 160, 48);

    expect(acts).toEqual([{ kind: "start" }]);
  });

  it("does nothing when the press is released off the button", () => {
    overlay.render(state({ phase: "ready", canStart: true }));
    pointAt(".ov-tile");
    overlay.pointer("down", 160, 48);
    pointAt(null);

    overlay.pointer("up", 300, 90);

    expect(acts).toEqual([]);
  });

  it("widens Listening on hover and reports the wider rectangle", () => {
    overlay.render(state({ phase: "listening", canCancel: true, showsTimer: true, listeningSinceMs: Date.now() }));
    pointAt(null);

    overlay.pointer("enter", 160, 48);

    expect(rects.at(-1)).toEqual({ x: 84, y: 32, width: 152, height: 32 });
    expect(stage.querySelector(".ov-vis")!.classList.contains("ov-hover")).toBe(true);
  });

  it("cancels from the hover ✕", () => {
    overlay.render(state({ phase: "listening", canCancel: true, listeningSinceMs: Date.now() }));
    pointAt('[data-layer="listening"] .ov-x');

    overlay.pointer("enter", 210, 48);
    overlay.pointer("down", 210, 48);
    overlay.pointer("up", 210, 48);

    expect(acts).toEqual([{ kind: "cancel" }]);
  });

  it("finishes from the hover tick beside the ✕", () => {
    overlay.render(state({ phase: "listening", canCancel: true, canFinish: true, listeningSinceMs: Date.now() }));
    pointAt('[data-layer="listening"] .ov-finish');

    overlay.pointer("enter", 190, 48);
    overlay.pointer("down", 190, 48);
    overlay.pointer("up", 190, 48);

    expect(acts).toEqual([{ kind: "finish" }]);
    expect(stage.querySelector('[data-layer="listening"] .ov-finish')!.getAttribute("aria-label")).toBe("Finish and paste");
  });

  it("does not finish when the view does not offer it", () => {
    overlay.render(state({ phase: "listening", canCancel: true, listeningSinceMs: Date.now() }));
    pointAt('[data-layer="listening"] .ov-finish');

    overlay.pointer("down", 190, 48);
    overlay.pointer("up", 190, 48);

    expect(acts).toEqual([]);
  });

  it("treats a click on the Transcribing pill, off the ✕, as a press", () => {
    overlay.render(state({ phase: "transcribing", canCancel: true }));
    pointAt(null);

    overlay.pointer("down", 150, 48);
    overlay.pointer("up", 150, 48);

    expect(acts).toEqual([{ kind: "press" }]);
  });

  it("does not count a Transcribing press that became a drag", () => {
    overlay.render(state({ phase: "transcribing", canCancel: true }));
    pointAt(null);

    overlay.pointer("down", 150, 48);
    overlay.drag(true);
    overlay.pointer("up", 200, 48);

    expect(acts).toEqual([]);
  });

  it("cancels, not presses, from the Transcribing ✕", () => {
    overlay.render(state({ phase: "transcribing", canCancel: true }));
    pointAt('[data-layer="transcribing"] .ov-x');

    overlay.pointer("down", 200, 48);
    overlay.pointer("up", 200, 48);

    expect(acts).toEqual([{ kind: "cancel" }]);
  });

  it("does not press on any other pill", () => {
    overlay.render(state({ phase: "listening", canCancel: true, listeningSinceMs: Date.now() }));
    pointAt(null);

    overlay.pointer("down", 150, 48);
    overlay.pointer("up", 150, 48);

    expect(acts).toEqual([]);
  });

  it("shows Still transcribing with a Cancel button that cancels", () => {
    overlay.render(state({ phase: "notice", notice: "stillTranscribing", canCancel: true }));
    pointAt('[data-layer="notice"] .ov-ghost');

    overlay.pointer("down", 200, 48);
    overlay.pointer("up", 200, 48);

    const layer = stage.querySelector('[data-layer="notice"]')!;
    expect(layer.textContent).toBe("Still transcribingCancel");
    expect(layer.classList.contains("ov-has-action")).toBe(true);
    expect(acts).toEqual([{ kind: "cancel" }]);
  });

  it("drops the Cancel button again for a notice without one", () => {
    overlay.render(state({ phase: "notice", notice: "takingLonger", canCancel: true }));
    overlay.render(state({ seq: 2, phase: "notice", notice: "noWordsHeard" }));

    const layer = stage.querySelector('[data-layer="notice"]')!;
    expect(layer.querySelector(".ov-ghost")).toBeNull();
    expect(layer.classList.contains("ov-has-action")).toBe(false);
  });

  it("retries and dismisses the Failed Dictation it shows", () => {
    overlay.render(state({ phase: "failed", canRetry: true, canDismiss: true, failureReason: "noInternet", failedId: "abc" }));
    pointAt(".ov-retry");
    overlay.pointer("down", 200, 48);
    overlay.pointer("up", 200, 48);
    pointAt('[data-layer="failed"] .ov-x');
    overlay.pointer("down", 240, 48);
    overlay.pointer("up", 240, 48);

    expect(stage.querySelector(".ov-reason")!.textContent).toBe("No internet");
    expect(acts).toEqual([
      { kind: "retry", failedId: "abc" },
      { kind: "dismiss", failedId: "abc" },
    ]);
  });

  it("shows Cancelled with an Undo button that undoes it (VED-76)", () => {
    overlay.render(state({ phase: "notice", notice: "cancelledUndo", canUndo: true, failedId: "abc" }));
    pointAt('[data-layer="notice"] .ov-ghost');

    overlay.pointer("down", 200, 48);
    overlay.pointer("up", 200, 48);

    const layer = stage.querySelector('[data-layer="notice"]')!;
    expect(layer.textContent).toBe("CancelledUndo");
    expect(layer.classList.contains("ov-has-action")).toBe(true);
    expect(acts).toEqual([{ kind: "undo", failedId: "abc" }]);
  });

  it("says the limit notices in words", () => {
    expect(noticeText("recordingLimitSoon", null)).toBe("Under a minute left to record");
    expect(noticeText("recordingLimitReached", null)).toBe("20 minute limit reached · Pasted");
    expect(liveText(state({ phase: "notice", notice: "cancelledUndo" }))).toBe("Cancelled");
  });

  it("ignores a button that belongs to another state", () => {
    overlay.render(state({ phase: "transcribing", canCancel: true }));
    pointAt(".ov-tile");

    overlay.pointer("down", 160, 48);
    overlay.pointer("up", 160, 48);

    // The Ready tile belongs to another layer, so nothing starts; the click lands on the pill.
    expect(acts).toEqual([{ kind: "press" }]);
  });

  it("opens Settings from the Add an API key notice", () => {
    overlay.render(state({ phase: "notice", notice: "addApiKey" }));
    pointAt(".ov-link");

    overlay.pointer("down", 160, 48);
    overlay.pointer("up", 160, 48);

    expect(acts).toEqual([{ kind: "openKeySettings" }]);
  });

  it("drops 12 px after a Cancel, and announces each state once", () => {
    overlay.render(state({ phase: "listening", canCancel: true, listeningSinceMs: Date.now() }));
    const live = stage.querySelector(".ov-live")!;
    expect(live.textContent).toBe("Listening");

    overlay.render(state({ seq: 2, phase: "hidden", afterCancel: true }));

    expect(stage.querySelector(".ov-vis")!.className).toContain("cancel");
    expect(live.textContent).toBe("");
  });
});
