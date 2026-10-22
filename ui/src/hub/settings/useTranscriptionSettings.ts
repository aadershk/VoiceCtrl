import { useEffect, useState } from "react";
import type { Bridge } from "../../bridge/bridge";
import type { TranscriptionSettingsView } from "../../bridge/contracts.g";

/**
 * The Transcription group's state from the host: Mode, Default cleanup, the Gemini key's state and
 * the Offline language packs, kept current through `settings.transcription` (which also carries a
 * pack download's progress). Null until the host answers; a host without these commands (an older
 * page fixture) just leaves it null.
 */
export function useTranscriptionSettings(bridge: Bridge): TranscriptionSettingsView | null {
  const [view, setView] = useState<TranscriptionSettingsView | null>(null);

  useEffect(() => {
    let live = true;
    const off = bridge.on("settings.transcription", ({ transcription }) => setView(transcription));
    bridge.call("settings.getTranscription", {}).then((v) => live && setView(v), () => undefined);
    return () => {
      live = false;
      off();
    };
  }, [bridge]);

  return view;
}
