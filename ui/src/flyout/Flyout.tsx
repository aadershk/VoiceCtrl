import { useEffect, useLayoutEffect, useRef, useState } from "react";
import type { KeyboardEvent } from "react";
import { flushSync } from "react-dom";
import type { Bridge } from "../bridge/bridge";
import type { AppSnapshot, TranscriptionModePreference, TrayCorner, TrayOpening } from "../bridge/contracts.g";
import { Logo, Menu, MenuItem, MenuSeparator } from "../design/components";
import { popoverEnter } from "../design/motion";
import { useAppState } from "../hub/useAppState";
import { flyoutStatusLine } from "./statusLine";

const MODES = [
  { value: "auto", label: "Auto" },
  { value: "online", label: "Online" },
  { value: "offline", label: "Offline" },
] as const satisfies readonly { value: TranscriptionModePreference; label: string }[];

const ORIGIN: Record<TrayCorner, "bottom right" | "bottom left" | "top right" | "top left"> = {
  bottomRight: "bottom right",
  bottomLeft: "bottom left",
  topRight: "top right",
  topLeft: "top left",
};

interface Opening extends TrayOpening {
  /** Counts openings, so each one replays the entrance even when nothing else changed. */
  n: number;
}

export interface FlyoutProps {
  bridge: Bridge;
  snapshot: AppSnapshot;
}

/**
 * The tray flyout (DESIGN §12): VoiceCtrl's own menu by the tray icon. The window is kept warm
 * and hidden between uses, so this page lives for the whole run. Each `tray.open` brings fresh
 * Mode and Copy latest state; the page lays itself out, grows in from the corner nearest the icon
 * and tells the host its size (`tray.place`), and the host then shows the window. Any pick, Esc
 * or a click outside the menu hides it again (`tray.close`); so does the window losing focus.
 */
export function Flyout({ bridge, snapshot }: FlyoutProps) {
  const app = useAppState(bridge, snapshot);
  const [opening, setOpening] = useState<Opening | null>(null);
  const [mode, setMode] = useState<TranscriptionModePreference>("auto");
  const frame = useRef<HTMLDivElement>(null);

  // flushSync: the window is hidden while this runs, and the host shows it only once the page
  // has answered with its size, so the layout must happen now rather than on a later frame.
  useEffect(
    () =>
      bridge.on("tray.open", (next) => {
        flushSync(() => {
          setOpening((current) => ({ ...next, n: (current?.n ?? 0) + 1 }));
          setMode(next.mode);
        });
      }),
    [bridge],
  );

  useLayoutEffect(() => {
    const el = frame.current;
    if (!opening || !el) return;
    popoverEnter(el, ORIGIN[opening.corner]);
    // Layout size, which the entrance's scale doesn't touch.
    void bridge.call("tray.place", { width: el.offsetWidth, height: el.offsetHeight });
  }, [bridge, opening]);

  // Each opening focuses the menu itself, with no item picked out, as Windows does for a menu
  // opened with the mouse: the arrow keys then go to the first or last item. A passive effect, so
  // it runs after the Menu's own focus on its first opening.
  useEffect(() => {
    const menu = frame.current?.querySelector<HTMLElement>('[role="menu"]');
    if (!opening || !menu) return;
    menu.tabIndex = -1;
    menu.focus({ preventScroll: true });
  }, [opening]);

  // A status line that wraps or unwraps while the flyout is open changes its height; the host
  // moves the window to match.
  useEffect(() => {
    const el = frame.current;
    if (!opening || !el || typeof ResizeObserver === "undefined") return;
    let height = el.offsetHeight;
    const observer = new ResizeObserver(() => {
      if (el.offsetHeight === height) return;
      height = el.offsetHeight;
      void bridge.call("tray.place", { width: el.offsetWidth, height });
    });
    observer.observe(el);
    return () => observer.disconnect();
  }, [bridge, opening]);

  if (!opening) return null;

  const close = () => void bridge.call("tray.close", {});
  const chooseMode = (next: TranscriptionModePreference) => {
    setMode(next);
    void bridge.call("settings.setMode", { mode: next });
  };
  const paused = app.status.paused;

  return (
    <div ref={frame} className="flyout">
      <Menu open placement="static" label="VoiceCtrl" onClose={close}>
        <div className="flyout-head">
          <span className="flyout-brand">
            <Logo size={22} />
            VoiceCtrl
          </span>
          <span className="flyout-status">{flyoutStatusLine(app.status)}</span>
        </div>
        <MenuSeparator />
        <MenuItem icon="window" onSelect={() => void bridge.call("tray.openHub", {})}>
          Open VoiceCtrl
        </MenuItem>
        <MenuItem icon="copy" disabled={!opening.hasLatest} onSelect={() => void bridge.call("tray.copyLatest", {})}>
          Copy latest Transcription
        </MenuItem>
        <MenuItem icon={paused ? "play" : "pause"} onSelect={() => void bridge.call("app.setPaused", { paused: !paused })}>
          {paused ? "Resume" : "Pause"}
        </MenuItem>
        <MenuSeparator />
        <div className="flyout-mode">
          <span id="flyout-mode-label">Mode</span>
          <ModeControl value={mode} onChange={chooseMode} />
        </div>
        <MenuSeparator />
        <MenuItem icon="settings" onSelect={() => void bridge.call("tray.openSettings", {})}>
          Settings
        </MenuItem>
        <MenuItem icon="power" onSelect={() => void bridge.call("tray.quit", {})}>
          Quit VoiceCtrl
        </MenuItem>
      </Menu>
    </div>
  );
}

/**
 * Mode as the segmented control, built from menu radio items so the menu's arrow keys reach it
 * like any other item (Left and Right also move within it). Picking a Mode keeps the menu open.
 */
function ModeControl({ value, onChange }: { value: TranscriptionModePreference; onChange: (mode: TranscriptionModePreference) => void }) {
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key !== "ArrowLeft" && event.key !== "ArrowRight") return;
    const buttons = Array.from(event.currentTarget.querySelectorAll<HTMLButtonElement>("button"));
    const at = buttons.indexOf(document.activeElement as HTMLButtonElement);
    const step = event.key === "ArrowRight" ? 1 : -1;
    event.preventDefault();
    buttons[(at + step + buttons.length) % buttons.length]?.focus();
  };

  return (
    <div className="vc-seg" role="group" aria-labelledby="flyout-mode-label" onKeyDown={onKeyDown}>
      {MODES.map((option) => (
        <button
          key={option.value}
          type="button"
          role="menuitemradio"
          tabIndex={-1}
          aria-checked={option.value === value}
          onClick={() => onChange(option.value)}
        >
          {option.label}
        </button>
      ))}
    </div>
  );
}
