import { afterEach, beforeEach, describe, expect, it } from "vitest";
import type { OverlayState } from "../src/bridge/contracts.g";
import { handleHitRect, handleRect, pillParts, pillRect } from "../src/overlay/layout";
import { createOverlay } from "../src/overlay/overlay";
import type { Overlay, OverlayAction } from "../src/overlay/overlay";

// Docking and the resting handle (research 11, Interaction; DESIGN §9). The pill positions must
// match C# OverlayGeometry.PillLeftDip, which places the window around them.

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

describe("Overlay docks", () => {
  it("grows from the centre at Bottom, rightwards at Left and leftwards at Right", () => {
    expect(pillRect(54, "bottom").x).toBe(160 - 27);
    expect(pillRect(54, "left").x).toBe(16);
    expect(pillRect(196, "left").x).toBe(16);
    expect(pillRect(54, "right").x + 54).toBe(320 - 16);
    expect(pillRect(196, "right").x + 196).toBe(320 - 16);
  });

  it("builds the pill's parts from the dock's anchor", () => {
    expect(pillParts(54, "left")).toEqual({ left: -0, midX: 10, midScale: 0.34, rightX: 43 });
    expect(pillParts(54, "right").left).toBe(-54);
  });

  it("rests the handle on the pill's bottom edge, placed like the pill", () => {
    expect(handleRect("bottom")).toEqual({ x: 136, y: 56, width: 48, height: 8 });
    expect(handleRect("left").x).toBe(16);
    expect(handleRect("right").x + 48).toBe(304);
  });

  it("hit-tests the handle and its Ready tile together under the pointer", () => {
    expect(handleHitRect("bottom", false)).toEqual(handleRect("bottom"));
    expect(handleHitRect("bottom", true)).toEqual({ x: 136, y: 32, width: 48, height: 32 });
    expect(handleHitRect("left", true)).toEqual({ x: 16, y: 32, width: 48, height: 32 });
  });
});

describe("Overlay page: handle and docks", () => {
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
  });

  const handle = () => stage.querySelector(".ov-handle")!;
  const vis = () => stage.querySelector(".ov-vis")!;
  const pointAt = (selector: string | null) => {
    document.elementFromPoint = () => (selector ? stage.querySelector(selector) : null);
  };

  it("rests as the handle with the pill out of sight", () => {
    overlay.render(state({ phase: "handle", canStart: true }));

    expect(handle().classList.contains("on")).toBe(true);
    expect(vis().classList.contains("on")).toBe(false);
    expect(rects.at(-1)).toEqual(handleRect("bottom"));
  });

  it("crossfades into the Ready tile under the pointer, and back when it leaves", () => {
    overlay.render(state({ phase: "handle", canStart: true }));
    pointAt(null);

    overlay.pointer("enter", 160, 60);
    expect(handle().classList.contains("on")).toBe(false);
    expect(vis().classList.contains("on")).toBe(true);
    expect(stage.querySelector('[data-layer="ready"]')!.classList.contains("on")).toBe(true);
    expect(rects.at(-1)).toEqual(handleHitRect("bottom", true));

    overlay.pointer("leave", 160, 10);
    expect(handle().classList.contains("on")).toBe(true);
    expect(vis().classList.contains("on")).toBe(false);
  });

  it("starts a Dictation from the handle's Ready tile", () => {
    overlay.render(state({ phase: "handle", canStart: true }));
    pointAt(".ov-tile");

    overlay.pointer("enter", 160, 48);
    overlay.pointer("down", 160, 48);
    overlay.pointer("up", 160, 48);

    expect(acts).toEqual([{ kind: "start" }]);
  });

  it("goes back to the handle after a Dictation, keeping the pill while the pointer is on it", () => {
    overlay.render(state({ phase: "pasted" }));
    overlay.render(state({ seq: 2, phase: "handle", canStart: true }));
    expect(vis().className).toContain("out");
    expect(handle().classList.contains("on")).toBe(true);

    overlay.render(state({ seq: 3, phase: "pasted" }));
    pointAt(null);
    overlay.pointer("enter", 160, 48);
    overlay.render(state({ seq: 4, phase: "handle", canStart: true }));
    expect(vis().classList.contains("on")).toBe(true);
    expect(handle().classList.contains("on")).toBe(false);
  });

  it("re-anchors the pill for a new dock and reports where it now is", () => {
    overlay.render(state({ phase: "listening", canCancel: true, listeningSinceMs: Date.now() }));

    overlay.layout({ dock: "left", desktop: "dark" });

    expect(rects.at(-1)).toEqual(pillRect(54, "left"));
    expect((stage as HTMLElement).dataset.desktop).toBe("dark");
    expect((stage.querySelector(".ov-pos") as HTMLElement).style.left).toBe("16px");
  });

  it("turns a press into a drag: the button is not clicked when it comes up", () => {
    overlay.render(state({ phase: "ready", canStart: true }));
    pointAt(".ov-tile");
    overlay.pointer("down", 160, 48);

    overlay.drag(true);
    overlay.drag(false);
    overlay.pointer("up", 160, 48);

    expect(acts).toEqual([]);
    expect(stage.querySelector(".is-pressed")).toBeNull();
  });
});
