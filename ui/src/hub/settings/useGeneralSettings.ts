import { useEffect, useState } from "react";
import type { Bridge } from "../../bridge/bridge";
import type { GeneralSettingsView } from "../../bridge/contracts.g";

/**
 * Sound and Overlay, Privacy and App from the host, kept current through `settings.general`
 * (which also carries an update check or download as it moves). Null until the host answers; a
 * host without these commands (an older page fixture) just leaves it null.
 */
export function useGeneralSettings(bridge: Bridge): GeneralSettingsView | null {
  const [view, setView] = useState<GeneralSettingsView | null>(null);

  useEffect(() => {
    let live = true;
    const off = bridge.on("settings.general", ({ general }) => setView(general));
    bridge.call("settings.getGeneral", {}).then((v) => live && setView(v), () => undefined);
    return () => {
      live = false;
      off();
    };
  }, [bridge]);

  return view;
}
