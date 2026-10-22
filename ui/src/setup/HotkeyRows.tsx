import { useEffect, useRef, useState } from "react";
import type { Bridge } from "../bridge/bridge";
import type { DictationSettingsView, KeyCaptureTarget } from "../bridge/contracts.g";
import { Button, Icon } from "../design/components";
import { useKeyCapture } from "../hub/settings/useDictationSettings";

interface KeyRowSpec {
  target: Extract<KeyCaptureTarget, "triggerKey" | "rewriteKey">;
  /** Names the Change button for screen readers, e.g. "Change talk keys". */
  noun: string;
  /** "Hold <keys> to talk". */
  verb: string;
  help: string;
}

const ROWS: readonly KeyRowSpec[] = [
  { target: "triggerKey", noun: "talk keys", verb: "talk", help: "Let go and it pastes. Esc cancels." },
  { target: "rewriteKey", noun: "rewrite keys", verb: "rewrite", help: "Select text, say the change, let go." },
];

/**
 * The hotkey step's two holds (VED-70): talk and rewrite, each shown as the sentence it teaches
 * with a Change button, plus the double-tap in one line. Nothing is forced: the defaults work as
 * they are. Changing runs the same host capture as Settings; a refused combination shows its hint
 * here while listening continues, and a combination Windows may also act on is warned about after
 * it is saved.
 */
export function HotkeyRows({ bridge, hotkey }: { bridge: Bridge; hotkey: string }) {
  const [view, setView] = useState<DictationSettingsView | null>(null);
  const [warning, setWarning] = useState<{ target: KeyCaptureTarget; text: string } | null>(null);
  const lastTarget = useRef<KeyCaptureTarget>("triggerKey");

  useEffect(() => {
    let live = true;
    const off = bridge.on("settings.dictation", ({ dictation }) => setView(dictation));
    bridge.call("settings.getDictation", {}).then((v) => live && setView(v), () => undefined);
    return () => {
      live = false;
      off();
    };
  }, [bridge]);

  const capture = useKeyCapture(bridge, (text) => setWarning(text ? { target: lastTarget.current, text } : null));

  if (!view) return null;
  return (
    <>
      <section className="vc-card setup-keys-card" aria-label="Your hotkeys">
        {ROWS.map((spec, index) => {
          const keys = spec.target === "triggerKey" ? view.triggerKey : view.rewriteKey;
          const listening = capture.target === spec.target;
          // The host's own notice (keys that are also a Windows shortcut on this PC) outranks the
          // generic one a save returns, and stays until the keys change (VED-74).
          const conflict = spec.target === "triggerKey" ? view.triggerKeyConflict : view.rewriteKeyConflict;
          const note = listening ? capture.hint : (conflict ?? (warning?.target === spec.target ? warning.text : null));
          const help = spec.help;
          return (
            <div key={spec.target}>
              {index > 0 && <div className="setup-sep" />}
              <div className="setup-row">
                <span className="setup-row-label">
                  <b>{keys ? `Hold ${keys.join(" + ")} to ${spec.verb}` : `Not set`}</b>
                  {note ? (
                    <span className="setup-note">
                      <Icon name="alert" size={16} />
                      {note}
                    </span>
                  ) : listening ? (
                    <span role="status">Press your combination</span>
                  ) : (
                    <span>{help}</span>
                  )}
                </span>
                {listening ? (
                  <Button variant="ghost" size="sm" onClick={capture.cancel}>
                    Cancel
                  </Button>
                ) : (
                  <Button
                    size="sm"
                    aria-label={`Change ${spec.noun}`}
                    onClick={() => {
                      lastTarget.current = spec.target;
                      setWarning(null);
                      capture.start(spec.target);
                    }}
                  >
                    Change
                  </Button>
                )}
              </div>
            </div>
          );
        })}
      </section>
      <p className="setup-cap">
        Or double-tap <b>{hotkey}</b> to bring up the bar. Change any of these later in Settings.
      </p>
    </>
  );
}
