import { useEffect, useState } from "react";
import type { Bridge } from "../bridge/bridge";
import type { AppSnapshot } from "../bridge/contracts.g";

export interface AppState extends AppSnapshot {
  maximized: boolean;
}

/**
 * The app-wide state a page renders: the `app.ready` snapshot, kept current by the host's events
 * (Theme, reduced motion, the status card, maximize). Theme and reduced motion are also written to
 * `<html>`, which is what the tokens follow; the host sets both before first paint, so this only
 * matters for live changes (Settings, the status card toggle, or Windows while Theme is System).
 */
export function useAppState(bridge: Bridge, snapshot: AppSnapshot): AppState {
  const [state, setState] = useState<AppState>({ ...snapshot, maximized: false });

  useEffect(() => {
    const off = [
      bridge.on("app.theme", ({ theme, themePreference }) => setState((s) => ({ ...s, theme, themePreference }))),
      bridge.on("app.reducedMotion", ({ reducedMotion }) => setState((s) => ({ ...s, reducedMotion }))),
      bridge.on("app.status", ({ status }) => setState((s) => ({ ...s, status }))),
      bridge.on("window.state", ({ maximized }) => setState((s) => ({ ...s, maximized }))),
    ];
    return () => off.forEach((stop) => stop());
  }, [bridge]);

  useEffect(() => {
    document.documentElement.dataset.theme = state.theme;
    document.documentElement.dataset.reducedMotion = String(state.reducedMotion);
  }, [state.theme, state.reducedMotion]);

  return state;
}
