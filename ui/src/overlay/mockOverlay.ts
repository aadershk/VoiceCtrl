/**
 * The Overlay on the mock host (spec §6), for `npm run dev`, the visual tests and the owner's
 * review page. The address picks the state, e.g. `overlay.html?state=failed&reason=timedOut&theme=dark`
 * or `?state=listening&hover=1&level=.6&elapsed=7`, and the layout with `dock=left|right` and
 * `desktop=light|dark` (the handle's tint). With no state it starts on the Ready tile (the handle
 * with `always=1`) and runs a small stand-in engine: click the tile and it listens, transcribes
 * and pastes.
 *
 * In a plain browser real pointer events stand in for the host's mouse hook: they are turned into
 * the same enter/move/down/up/leave calls, against the rectangle the page reported.
 */
import { createMockHost } from "../bridge/mock";
import type { MockHost } from "../bridge/mock";
import type { FailureReason, OverlayDock, OverlayLayout, OverlayNotice, OverlayPhase, OverlayState, Theme } from "../bridge/contracts.g";
import { pillRect } from "./layout";
import type { Overlay } from "./overlay";

export interface OverlayMock {
  host: MockHost;
  /** Connects real pointer events and the address's hover and level to the running Overlay. */
  attach(overlay: Overlay, stage: HTMLElement): void;
}

interface Rect {
  x: number;
  y: number;
  width: number;
  height: number;
}

const PHASES: readonly OverlayPhase[] = ["hidden", "handle", "ready", "listening", "transcribing", "pasted", "noSpeech", "failed", "notice"];
const DOCKS: readonly OverlayDock[] = ["bottom", "left", "right"];

export function installOverlayMock(search: string): OverlayMock {
  const query = new URLSearchParams(search);
  // Without a theme in the address, the page's own default (a review page may set it on <html>).
  const theme: Theme = (query.get("theme") ?? document.documentElement.dataset.theme) === "dark" ? "dark" : "light";
  const reducedMotion = query.get("rm") === "1";
  document.documentElement.dataset.theme = theme;
  const host = createMockHost({ theme, themePreference: theme, reducedMotion });

  const requested = query.get("state") as OverlayPhase | null;
  const scripted = requested !== null && PHASES.includes(requested);
  const reason = (query.get("reason") ?? "noInternet") as FailureReason;
  const notice = (query.get("notice") ?? "micUnavailable") as OverlayNotice;
  const elapsedMs = Number(query.get("elapsed") ?? "7") * 1000;
  const rewriteScripted = query.get("rewrite") === "1";
  const lockedScripted = query.get("locked") === "1";
  const writingScripted = query.get("writing") === "1";
  const always = query.get("always") === "1" || requested === "handle";
  const requestedDock = query.get("dock") as OverlayDock | null;
  let layout: OverlayLayout = {
    dock: requestedDock !== null && DOCKS.includes(requestedDock) ? requestedDock : "bottom",
    desktop: (query.get("desktop") ?? theme) === "dark" ? "dark" : "light",
  };

  let seq = 0;
  let timers: number[] = [];
  let levelTimer = 0;
  let hitRect: Rect | null = null;

  const view = (phase: OverlayPhase, afterCancel = false, shownNotice: OverlayNotice = notice): OverlayState => ({
    seq: ++seq,
    phase,
    canStart: phase === "ready" || phase === "handle",
    showsTimer: phase === "listening",
    canCancel:
      phase === "listening" ||
      phase === "transcribing" ||
      (phase === "notice" && (shownNotice === "stillTranscribing" || shownNotice === "stillWriting" || shownNotice === "takingLonger")),
    canFinish: phase === "listening",
    canUndo: phase === "notice" && shownNotice === "cancelledUndo",
    canRetry: phase === "failed",
    canDismiss: phase === "failed",
    listeningSinceMs: phase === "listening" ? Date.now() - (scripted ? elapsedMs : 0) : null,
    failureReason: phase === "failed" ? reason : null,
    failedId: phase === "failed" || (phase === "notice" && shownNotice === "cancelledUndo") ? "00000000-0000-0000-0000-000000000001" : null,
    notice: phase === "notice" ? shownNotice : null,
    noticeKey: phase === "notice" && shownNotice === "pasteFailed" ? query.get("key") : null,
    afterCancel,
    rewrite: rewriteScripted && (phase === "listening" || phase === "transcribing"),
    locked: lockedScripted && phase === "listening",
    writing: writingScripted && phase === "transcribing",
  });

  const resting: OverlayPhase = always ? "handle" : "ready";
  let current = view(scripted ? requested : resting);

  const clear = () => {
    timers.forEach(clearTimeout);
    timers = [];
    clearInterval(levelTimer);
  };
  const later = (ms: number, run: () => void) => timers.push(window.setTimeout(run, ms));
  const show = (phase: OverlayPhase, afterCancel = false, shownNotice?: OverlayNotice) => {
    current = view(phase, afterCancel, shownNotice);
    host.emit("overlay.view", { state: current });
    if (phase === "listening") {
      // A made-up voice, so the bars move as they would while someone speaks.
      let n = 0;
      levelTimer = window.setInterval(() => {
        n++;
        host.emit("overlay.level", { level: 0.15 + 0.7 * Math.abs(Math.sin(n * 0.21) * Math.sin(n * 0.07 + 1)) });
      }, 16);
    }
  };
  const rest = () => show(resting);
  const transcribeAndPaste = () => {
    show("transcribing");
    later(1500, () => show("pasted"));
    later(2400, rest);
  };

  host.handle("overlay.state", () => current);
  host.handle("overlay.layout", () => layout);
  host.handle("overlay.hitRect", (rect) => {
    hitRect = rect.width > 0 && rect.height > 0 ? rect : null;
    return null;
  });
  host.handle("overlay.painted", () => null);
  host.handle("overlay.start", () => {
    clear();
    show("listening");
    later(3600, () => {
      clearInterval(levelTimer);
      transcribeAndPaste();
    });
    return null;
  });
  host.handle("overlay.finish", () => {
    clear();
    transcribeAndPaste();
    return null;
  });
  host.handle("overlay.press", () => {
    clear();
    show("notice", false, "stillTranscribing");
    later(2500, () => show("transcribing"));
    later(4000, () => show("pasted"));
    later(4900, rest);
    return null;
  });
  host.handle("overlay.cancel", () => {
    clear();
    if (always) show("handle", true);
    else {
      show("hidden", true);
      later(700, rest);
    }
    return null;
  });
  host.handle("overlay.retry", () => {
    clear();
    transcribeAndPaste();
    return null;
  });
  host.handle("overlay.undo", () => {
    clear();
    transcribeAndPaste();
    return null;
  });
  host.handle("overlay.dismiss", () => {
    clear();
    rest();
    return null;
  });
  host.handle("overlay.openKeySettings", () => null);

  return {
    host,
    attach(overlay, stage) {
      const toStage = (event: PointerEvent) => {
        const origin = stage.getBoundingClientRect();
        return { x: event.clientX - origin.left, y: event.clientY - origin.top };
      };
      // The host's rule: the page's rectangle plus 4 px (C# OverlayGeometry.HitMarginDip).
      const within = (p: { x: number; y: number }) =>
        hitRect !== null &&
        p.x >= hitRect.x - 4 && p.x < hitRect.x + hitRect.width + 4 &&
        p.y >= hitRect.y - 4 && p.y < hitRect.y + hitRect.height + 4;
      let inside = false;
      let pressed = false;
      document.addEventListener("pointermove", (event) => {
        const p = toStage(event);
        const now = within(p);
        if (now !== inside) {
          inside = now;
          overlay.pointer(now ? "enter" : "leave", p.x, p.y);
        }
        if (now) overlay.pointer("move", p.x, p.y);
      });
      document.addEventListener("pointerdown", (event) => {
        const p = toStage(event);
        if (!within(p)) return;
        pressed = true;
        overlay.pointer("down", p.x, p.y);
      });
      document.addEventListener("pointerup", (event) => {
        if (!pressed) return;
        pressed = false;
        const p = toStage(event);
        overlay.pointer("up", p.x, p.y);
      });

      if (query.get("hover") === "1") {
        const tile = pillRect(32, layout.dock);
        overlay.pointer("enter", tile.x + tile.width / 2, tile.y + tile.height / 2);
      }
      const level = query.get("level");
      if (level !== null) host.emit("overlay.level", { level: Number(level) });

      // The review page drives the states from outside the frame.
      (window as { overlayDemo?: unknown }).overlayDemo = {
        show: (phase: OverlayPhase) => {
          clear();
          show(phase);
        },
        layout: (next: Partial<OverlayLayout>) => {
          layout = { ...layout, ...next };
          host.emit("overlay.layoutChanged", { layout });
        },
      };
    },
  };
}
