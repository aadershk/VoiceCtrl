/**
 * The Overlay page (spec §6–7, DESIGN §9–10): plain TypeScript, no framework, because it is one
 * animated element on the latency-critical path. A pure function of the latest `OverlayState`,
 * plus the live level and pointer input the host forwards.
 *
 * Build rules from the spike (research 15): the pill is two caps and a middle scaled on X; only
 * `transform` and `opacity` animate; the page never gets native pointer events, so hover, press
 * and click are worked out here from forwarded points (`pointer`). The host moves the window for
 * a drag and a dock change; the page only re-anchors the pill for the dock (`layout`).
 */
import type { OverlayLayout, OverlayPointerKind, OverlayState } from "../bridge/contracts.g";
import { iconMarkup } from "../design/icons";
import {
  anchorX,
  barScales,
  FAILURE_TEXT,
  formatElapsed,
  growth,
  HANDLE,
  handleHitRect,
  isMeasured,
  LAYERS,
  layerFor,
  liveText,
  NOTICES,
  noticeText,
  pillParts,
  pillRect,
  pillWidth,
  revealsOnHover,
  smooth,
  WIDTHS,
} from "./layout";
import type { Layer } from "./layout";

export type OverlayAction =
  | { kind: "start" | "cancel" | "finish" | "press" | "openKeySettings" }
  | { kind: "retry" | "dismiss" | "undo"; failedId: string };

export interface OverlayHostPort {
  /** A click on one of the pill's buttons. */
  act(action: OverlayAction): void;
  /** The pill's rectangle in stage pixels (zero size: nothing to hit). */
  reportHitRect(rect: { x: number; y: number; width: number; height: number }): void;
  /** View `seq` is on screen (after the second animation frame). */
  painted(seq: number): void;
}

const EXIT_CLEANUP_MS = 260;
const REDUCED_LEVEL_INTERVAL_MS = 200;

const bars = (cls: string) =>
  `<span class="ov-bars ${cls}" aria-hidden="true"><span class="ov-bar"></span><span class="ov-bar"></span><span class="ov-bar"></span></span>`;
const closeIcon = iconMarkup("close", 14);
const finishIcon = iconMarkup("check", 14);
const lockIcon = iconMarkup("overlayLock", 14);

const MARKUP = `
<div class="ov-pos">
  <div class="ov-handle" aria-hidden="true"></div>
  <div class="ov-vis">
    <div class="ov-shape" aria-hidden="true"><div class="ov-part ov-cap-l"></div><div class="ov-part ov-mid"></div><div class="ov-part ov-cap-r"></div></div>
    <div class="ov-layer" data-layer="ready">
      <button class="ov-tile" data-action="start" aria-label="Start Dictation">${bars("ov-logo")}</button>
    </div>
    <div class="ov-layer" data-layer="listening">
      <span class="ov-dot" aria-hidden="true"></span>${bars("ov-level")}<span class="ov-lock" aria-hidden="true">${lockIcon}</span>
      <span class="ov-timer ov-hx" aria-hidden="true">0:00</span><span class="ov-sep ov-hx"></span>
      <button class="ov-finish ov-hx" data-action="finish" aria-label="Finish and paste">${finishIcon}</button>
      <button class="ov-x ov-hx" data-action="cancel" aria-label="Cancel (Esc)">${closeIcon}</button>
    </div>
    <div class="ov-layer" data-layer="transcribing">
      ${bars("ov-wave")}<span class="ov-writing-words" aria-hidden="true">Writing email</span><span class="ov-track" aria-hidden="true"><span class="ov-trace"></span></span>
      <span class="ov-sep ov-hx"></span>
      <button class="ov-x ov-hx" data-action="cancel" aria-label="Cancel (Esc)">${closeIcon}</button>
    </div>
    <div class="ov-layer" data-layer="pasted">
      <svg class="ov-check" viewBox="0 0 24 24" role="img" aria-label="Pasted"><path d="M5 12.5l4.5 4.5L19 7.5"/></svg>
    </div>
    <div class="ov-layer" data-layer="noSpeech">${iconMarkup("overlayMicOff", 14)}<span>No speech</span></div>
    <div class="ov-layer" data-layer="failed">
      ${iconMarkup("overlayAlert", 14)}<span class="ov-reason"></span>
      <button class="ov-retry" data-action="retry">Retry</button><span class="ov-sep"></span>
      <button class="ov-x" data-action="dismiss" aria-label="Dismiss Failed Dictation">${closeIcon}</button>
    </div>
    <div class="ov-layer" data-layer="notice"><span class="ov-notice"></span></div>
  </div>
</div>
<div class="ov-live" role="status" aria-live="polite"></div>`;

export interface Overlay {
  render(state: OverlayState): void;
  level(level: number): void;
  pointer(kind: OverlayPointerKind, x: number, y: number): void;
  /** The dock (which way the pill grows) and the desktop's light or dark setting (the handle's tint). */
  layout(layout: OverlayLayout): void;
  /** The host has turned the current press into a drag (true), or ended it. */
  drag(dragging: boolean): void;
  dispose(): void;
}

function reducedMotion(): boolean {
  return (
    document.documentElement.dataset.reducedMotion === "true" ||
    (typeof matchMedia === "function" && matchMedia("(prefers-reduced-motion: reduce)").matches)
  );
}

/** Builds the Overlay inside `stage` (the window-sized box) and returns its controls. */
export function createOverlay(stage: HTMLElement, host: OverlayHostPort): Overlay {
  stage.innerHTML = MARKUP;
  const q = <T extends Element>(selector: string) => stage.querySelector<T>(selector)!;
  const pos = q<HTMLElement>(".ov-pos");
  const vis = q<HTMLElement>(".ov-vis");
  const handle = q<HTMLElement>(".ov-handle");
  const capL = q<HTMLElement>(".ov-cap-l");
  const mid = q<HTMLElement>(".ov-mid");
  const capR = q<HTMLElement>(".ov-cap-r");
  const live = q<HTMLElement>(".ov-live");
  const timer = q<HTMLElement>(".ov-timer");
  const reason = q<HTMLElement>(".ov-reason");
  const notice = q<HTMLElement>(".ov-notice");
  const levelBars = [...stage.querySelectorAll<HTMLElement>(".ov-level .ov-bar")];
  const layers = new Map(LAYERS.map((layer) => [layer, q<HTMLElement>(`[data-layer="${layer}"]`)]));

  let state: OverlayState | null = null;
  let layer: Layer = "ready";
  let dock: OverlayLayout["dock"] = "bottom";
  let hovered = false;
  let hoverTarget: HTMLElement | null = null;
  let pressed: HTMLElement | null = null;
  /** A press that began on the Transcribing pill's body rather than on a button. */
  let bodyPress = false;
  let exitTimer = 0;
  let clockTimer = 0;
  let levelTarget = 0;
  let shown = [0, 0, 0];
  let levelFrame = 0;
  let levelInterval = 0;

  // Notices and Failed are as wide as their words; the layers size to their content.
  const measure = (name: Layer) => Math.ceil(layers.get(name)!.scrollWidth);

  function applyGeometry(width: number) {
    const parts = pillParts(width, dock);
    const k = growth(dock);
    capL.style.transform = `translateX(${parts.left}px)`;
    mid.style.transform = `translateX(${parts.midX}px) scaleX(${parts.midScale})`;
    capR.style.transform = `translateX(${parts.rightX}px)`;
    for (const [name, el] of layers) {
      // The current layer starts at the pill's left edge; the others wait where their own width
      // would put them on this dock, so each slides in from where it naturally sits.
      const own = isMeasured(name) ? pillWidth(name, false, measure(name)) : WIDTHS[name].rest;
      el.style.transform = `translateX(${-(name === layer ? width : own) * k}px)`;
    }
  }

  function currentWidth() {
    return pillWidth(layer, hovered, isMeasured(layer) ? measure(layer) : 0, state?.locked === true, state?.writing === true);
  }

  function reportHitRect() {
    if (state === null || state.phase === "hidden") host.reportHitRect({ x: 0, y: 0, width: 0, height: 0 });
    else if (state.phase === "handle") host.reportHitRect(handleHitRect(dock, hovered));
    else host.reportHitRect(pillRect(currentWidth(), dock));
  }

  /** While the handle rests, the pill (its Ready tile) shows only under the pointer. */
  const pillShown = () => state !== null && state.phase !== "hidden" && (state.phase !== "handle" || hovered);

  /** The handle and its Ready tile crossfade (DESIGN §9); the handle narrows as it goes. */
  function applyHandle() {
    const resting = state?.phase === "handle";
    vis.classList.toggle("ov-from-handle", resting);
    handle.classList.toggle("on", resting && !hovered);
    vis.classList.toggle("on", pillShown());
  }

  /** Applies a geometry change with no transition (the pill is invisible, so nothing should morph). */
  function jumpTo(width: number) {
    vis.classList.add("ov-instant");
    applyGeometry(width);
    void vis.offsetWidth;
    vis.classList.remove("ov-instant");
  }

  function fillContent(next: OverlayState) {
    if (next.phase === "failed") reason.textContent = FAILURE_TEXT[next.failureReason ?? "serviceError"];
    if (next.phase === "notice" && next.notice) {
      const copy = NOTICES[next.notice];
      const content = `${copy.icon ? iconMarkup(copy.icon, 14) : ""}<span>${noticeText(next.notice, next.noticeKey)}</span>`;
      notice.innerHTML = copy.opensKeySettings
        ? `<button class="ov-link" data-action="openKeySettings">${content}</button>`
        : copy.hasCancel
          ? `${content}<button class="ov-ghost" data-action="cancel">Cancel</button>`
          : copy.hasUndo
            ? `${content}<button class="ov-ghost" data-action="undo">Undo</button>`
            : content;
      layers.get("notice")!.classList.toggle("ov-has-action", copy.hasCancel === true || copy.hasUndo === true);
    }
  }

  function updateClock() {
    if (state?.phase === "listening" && state.listeningSinceMs !== null) {
      timer.textContent = formatElapsed(Date.now() - state.listeningSinceMs);
    }
  }

  function setBars(scales: readonly number[]) {
    levelBars.forEach((bar, i) => {
      bar.style.transform = `scaleY(${(scales[i] ?? 1).toFixed(3)})`;
    });
  }

  function startLevel() {
    stopLevel();
    shown = barScales(0);
    setBars(shown);
    if (reducedMotion()) {
      // Information keeps moving under reduced motion, unsmoothed, about 5 times a second (§10).
      levelInterval = window.setInterval(() => setBars(barScales(levelTarget)), REDUCED_LEVEL_INTERVAL_MS);
      return;
    }
    const frame = () => {
      const target = barScales(levelTarget);
      shown = shown.map((v, i) => smooth(v, target[i] ?? v));
      setBars(shown);
      levelFrame = requestAnimationFrame(frame);
    };
    levelFrame = requestAnimationFrame(frame);
  }

  function stopLevel() {
    cancelAnimationFrame(levelFrame);
    clearInterval(levelInterval);
    levelFrame = 0;
    levelInterval = 0;
    levelTarget = 0;
  }

  function setHoverTarget(el: HTMLElement | null) {
    if (el === hoverTarget) return;
    hoverTarget?.classList.remove("is-hover");
    hoverTarget = el;
    hoverTarget?.classList.add("is-hover");
  }

  function clearPointer() {
    setHoverTarget(null);
    pressed?.classList.remove("is-pressed");
    pressed = null;
    bodyPress = false;
  }

  function setHovered(next: boolean) {
    if (next === hovered) return;
    hovered = next;
    vis.classList.toggle("ov-hover", hovered);
    if (!hovered) clearPointer();
    if (state?.phase === "handle") {
      applyHandle();
      reportHitRect();
    } else if (state && state.phase !== "hidden" && revealsOnHover(layer)) {
      applyGeometry(currentWidth());
      reportHitRect();
    }
  }

  /** The enabled button under a stage point, if the current layer has one there. */
  function buttonAt(x: number, y: number): HTMLElement | null {
    const origin = stage.getBoundingClientRect();
    const hit = document.elementFromPoint(origin.left + x, origin.top + y);
    const button = hit?.closest<HTMLElement>("button[data-action]") ?? null;
    return button && stage.contains(button) && layers.get(layer)?.contains(button) ? button : null;
  }

  function activate(button: HTMLElement) {
    const action = button.dataset.action;
    if (!state) return;
    if (action === "start" && state.canStart) host.act({ kind: "start" });
    else if (action === "cancel" && state.canCancel) host.act({ kind: "cancel" });
    else if (action === "finish" && state.canFinish) host.act({ kind: "finish" });
    else if (action === "openKeySettings") host.act({ kind: "openKeySettings" });
    else if ((action === "retry" && state.canRetry) || (action === "dismiss" && state.canDismiss) || (action === "undo" && state.canUndo)) {
      if (state.failedId) host.act({ kind: action, failedId: state.failedId });
    }
  }

  // Words are measured when they are shown; a font that arrives later changes their width. Only the
  // notices that end in Cancel are re-measured: the other layers keep the widths their approved
  // screenshots were taken at.
  void document.fonts?.ready.then(() => {
    if (state && state.phase !== "hidden" && state.phase !== "handle" && layers.get("notice")!.classList.contains("ov-has-action") && layer === "notice") {
      applyGeometry(currentWidth());
      reportHitRect();
    }
  });

  return {
    render(next) {
      const previous = state;
      state = next;
      const nextLayer = layerFor(next.phase);
      clearTimeout(exitTimer);

      // The pill was on screen before this view: any phase but hidden, and the handle only
      // under the pointer.
      const wasShown = previous !== null && previous.phase !== "hidden" && (previous.phase !== "handle" || hovered);
      if (nextLayer && (next.phase !== "handle" || hovered)) {
        layer = nextLayer;
        fillContent(next);
        for (const [name, el] of layers) el.classList.toggle("on", name === layer);
        vis.classList.toggle("ov-nudge", layer === "failed");
        if (!wasShown) {
          jumpTo(currentWidth());
          vis.classList.remove("out", "cancel");
        } else {
          applyGeometry(currentWidth());
        }
      } else {
        if (wasShown) {
          vis.classList.add(next.afterCancel ? "cancel" : "out");
          // Back to the resting pose once the exit has played, so the next show rises from it.
          exitTimer = window.setTimeout(() => vis.classList.remove("out", "cancel"), EXIT_CLEANUP_MS);
        }
        if (nextLayer) {
          // The handle's tile waits, already the Ready tile, for the pointer to reach it; a pill
          // that is leaving shrinks into it as it fades. The pointer may still be over the
          // handle, so its hover is kept.
          layer = nextLayer;
          for (const [name, el] of layers) el.classList.toggle("on", name === layer);
          vis.classList.remove("ov-nudge");
          if (wasShown) applyGeometry(currentWidth());
          else jumpTo(currentWidth());
        } else {
          hovered = false;
          vis.classList.remove("ov-hover");
          clearPointer();
        }
      }
      applyHandle();

      // A rewrite by voice looks like a Dictation, with the recording red set aside (overlay.css).
      vis.classList.toggle("ov-rewrite", next.rewrite);
      vis.classList.toggle("ov-locked", next.locked);
      // An Email request's email being written (ticket 57): Transcribing, saying so.
      vis.classList.toggle("ov-writing", next.writing);

      if (previous?.phase !== next.phase || previous?.notice !== next.notice || previous?.failureReason !== next.failureReason || previous?.rewrite !== next.rewrite || previous?.locked !== next.locked || previous?.writing !== next.writing) {
        live.textContent = liveText(next);
      }

      if (next.phase === "listening") {
        if (previous?.phase !== "listening") startLevel();
        updateClock();
        if (!clockTimer) clockTimer = window.setInterval(updateClock, 250);
      } else {
        stopLevel();
        clearInterval(clockTimer);
        clockTimer = 0;
      }

      reportHitRect();
      requestAnimationFrame(() => requestAnimationFrame(() => host.painted(next.seq)));
    },

    level(value) {
      levelTarget = value;
    },

    layout(next) {
      stage.dataset.desktop = next.desktop;
      if (next.dock === dock && stage.dataset.dock === dock) return;
      dock = next.dock;
      stage.dataset.dock = dock;
      // The host moves the window by the same amount, so the pill stays where it is on screen.
      const k = growth(dock);
      pos.style.left = `${anchorX(dock)}px`;
      handle.style.left = `${-HANDLE.width * k}px`;
      handle.style.transformOrigin = `${k * 100}% 50%`;
      if (state) jumpTo(currentWidth());
      reportHitRect();
    },

    drag(dragging) {
      // The press became a drag, so its button won't be clicked when it comes up.
      if (dragging) {
        pressed?.classList.remove("is-pressed");
        pressed = null;
        bodyPress = false;
      }
    },

    pointer(kind, x, y) {
      if (!state || state.phase === "hidden") {
        if (kind === "leave") setHovered(false);
        return;
      }
      switch (kind) {
        case "enter":
          setHovered(true);
          setHoverTarget(buttonAt(x, y));
          break;
        case "leave":
          setHovered(false);
          break;
        case "move":
          setHoverTarget(buttonAt(x, y));
          break;
        case "down":
          pressed = buttonAt(x, y);
          pressed?.classList.add("is-pressed");
          // A click on the Transcribing pill itself (not on ✕) is a press while Transcribing.
          bodyPress = pressed === null && state.phase === "transcribing";
          break;
        case "up": {
          const target = buttonAt(x, y);
          const was = pressed;
          const body = bodyPress;
          pressed?.classList.remove("is-pressed");
          pressed = null;
          bodyPress = false;
          if (was && was === target) activate(was);
          else if (body && !was && !target) host.act({ kind: "press" });
          break;
        }
      }
    },

    dispose() {
      stopLevel();
      clearInterval(clockTimer);
      clearTimeout(exitTimer);
      stage.innerHTML = "";
    },
  };
}
