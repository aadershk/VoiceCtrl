import { useEffect, useState } from "react";
import type { Bridge } from "../../bridge/bridge";
import type { DictationsView } from "../../bridge/contracts.g";

/**
 * What the Dictations screen shows, from the host: the kept Dictations and Failed Dictations,
 * Keep Dictations and the Paste Again key. Kept current by `dictations.changed`. Null until the
 * host answers.
 */
export function useDictations(bridge: Bridge): DictationsView | null {
  const [view, setView] = useState<DictationsView | null>(null);

  useEffect(() => {
    let live = true;
    const off = bridge.on("dictations.changed", ({ dictations }) => setView(dictations));
    bridge.call("dictations.get", {}).then((v) => live && setView(v), () => undefined);
    return () => {
      live = false;
      off();
    };
  }, [bridge]);

  return view;
}

/** The current time, moved on every `everyMs`, so "2 min ago" and day groups stay true. */
export function useNow(everyMs = 30_000): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), everyMs);
    return () => clearInterval(timer);
  }, [everyMs]);
  return now;
}
