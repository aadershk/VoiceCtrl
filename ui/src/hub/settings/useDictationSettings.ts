import { useCallback, useEffect, useRef, useState } from "react";
import type { Bridge } from "../../bridge/bridge";
import type { DictationSettingsView, KeyCaptureTarget, MicrophonesView } from "../../bridge/contracts.g";

/**
 * The Dictation group's state from the host: the saved microphone and keys, and the microphones
 * Windows has. Both stay current through `settings.dictation` and `mic.devices`. Null until the
 * host answers; a host without these commands (an older page fixture) just leaves them null.
 */
export function useDictationSettings(bridge: Bridge) {
  const [view, setView] = useState<DictationSettingsView | null>(null);
  const [mics, setMics] = useState<MicrophonesView | null>(null);

  useEffect(() => {
    let live = true;
    const off = [
      bridge.on("settings.dictation", ({ dictation }) => setView(dictation)),
      bridge.on("mic.devices", ({ microphones }) => setMics(microphones)),
    ];
    bridge.call("settings.getDictation", {}).then((v) => live && setView(v), () => undefined);
    bridge.call("mic.list", {}).then((m) => live && setMics(m), () => undefined);
    return () => {
      live = false;
      off.forEach((stop) => stop());
    };
  }, [bridge]);

  return { view, mics };
}

export interface KeyCaptureState {
  /** The row that says "Press a key", or null. */
  target: KeyCaptureTarget | null;
  /** Why the last keys weren't used, shown in place of the row's description. */
  hint: string | null;
  start(target: KeyCaptureTarget): void;
  cancel(): void;
}

/**
 * "Press a key" for the Hotkey, Trigger Key and Paste Again rows. The host does the listening
 * (its keyboard hook sees Win and Alt, and swallows the keys so nothing else gets them); the page
 * shows which row is listening and any hint. One row at a time: starting another ends the first.
 */
export function useKeyCapture(bridge: Bridge, onSaved: (warning: string | null) => void): KeyCaptureState {
  const [target, setTarget] = useState<KeyCaptureTarget | null>(null);
  const [hint, setHint] = useState<string | null>(null);
  const run = useRef(0);
  const listening = useRef(false);
  const saved = useRef(onSaved);
  saved.current = onSaved;

  useEffect(() => bridge.on("settings.captureHint", ({ message }) => setHint(message)), [bridge]);

  // Leaving Settings mid-capture stops the listening, or keys would keep vanishing.
  useEffect(
    () => () => {
      if (listening.current) void bridge.call("settings.cancelCapture", {}).catch(() => undefined);
    },
    [bridge],
  );

  const start = useCallback(
    (next: KeyCaptureTarget) => {
      const mine = ++run.current;
      listening.current = true;
      setTarget(next);
      setHint(null);
      const done = (outcome: { saved: boolean; warning: string | null } | null) => {
        // A newer capture replaced this one; its answer ("not saved") changes nothing on screen.
        if (mine !== run.current) return;
        listening.current = false;
        setTarget(null);
        setHint(null);
        if (outcome?.saved) saved.current(outcome.warning);
      };
      bridge.call("settings.captureKey", { target: next }).then(done, () => done(null));
    },
    [bridge],
  );

  const cancel = useCallback(() => {
    void bridge.call("settings.cancelCapture", {}).catch(() => undefined);
  }, [bridge]);

  return { target, hint, start, cancel };
}
