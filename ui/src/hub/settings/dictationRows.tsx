import type { ReactNode } from "react";
import type { KeyCaptureTarget } from "../../bridge/contracts.g";
import { Switch } from "../../design/components";
import { useHub } from "../hubContext";
import { KeyControl } from "./KeyControl";
import { isUnplugged, MicrophoneControl } from "./MicrophoneControl";
import { useDictationSettings, useKeyCapture } from "./useDictationSettings";

/** What a Settings group gives the page: a control per row id, and a note that replaces a row's
 * description while something needs saying (a key hint, an unplugged microphone). A group may also
 * restate a row's plain description from live state (a download's progress); a note wins over it. */
export interface GroupRows {
  controls: Record<string, ReactNode>;
  notes: Record<string, ReactNode>;
  descriptions?: Record<string, ReactNode>;
  /** A label that says more than the row's own, e.g. "NVIDIA API key" for "API key". */
  labels?: Record<string, string>;
  /** Rows that don't apply in the current state (Other's address while Gemini is chosen). */
  hidden?: readonly string[];
}

const KEY_ROWS: readonly { id: string; target: KeyCaptureTarget; name: string; prompt: string }[] = [
  { id: "hotkey", target: "hotkey", name: "Hotkey", prompt: "Press a key" },
  { id: "trigger-key", target: "triggerKey", name: "Trigger Key", prompt: "Press your combination" },
  { id: "rewrite-key", target: "rewriteKey", name: "Rewrite key", prompt: "Press your combination" },
  { id: "paste-again", target: "pasteAgain", name: "Paste Again", prompt: "Press the keys" },
];

/**
 * The Dictation group (slice 20): Microphone, Hotkey, Trigger Key and Paste Again, plus the Press
 * Enter switch and, in the Sound and Overlay group, Mute Other Audio (slice 21), both settings of
 * the host's Dictation section. Every change is saved by the host and applies to the next
 * Dictation; the rows follow `settings.dictation`.
 */
export function useDictationRows(): GroupRows {
  const { bridge, isLanding, toast } = useHub();
  const { view, mics } = useDictationSettings(bridge);
  const capture = useKeyCapture(bridge, (warning) => {
    if (warning) toast(`Saved. ${warning}`);
  });

  if (!view || !mics) return { controls: {}, notes: {} };

  const controls: Record<string, ReactNode> = {
    microphone: (
      <MicrophoneControl
        bridge={bridge}
        view={view}
        mics={mics}
        landing={isLanding("microphone")}
        onPick={(id) => void bridge.call("settings.setMicrophone", { id })}
      />
    ),
    "press-enter": (
      <Switch label="Press Enter" checked={view.pressEnter} onChange={(on) => void bridge.call("settings.setPressEnter", { on })} />
    ),
    "mute-other-audio": (
      <Switch
        label="Mute Other Audio"
        checked={view.muteOtherAudio}
        onChange={(on) => void bridge.call("settings.setMuteOtherAudio", { on })}
      />
    ),
  };
  const notes: Record<string, ReactNode> = {};

  if (isUnplugged(view, mics) && mics.devices.length > 0) {
    notes.microphone = "Not connected, so VoiceCtrl is using the Windows default until it's back.";
  }

  for (const row of KEY_ROWS) {
    const keys =
      row.target === "hotkey"
        ? view.hotkey
        : row.target === "triggerKey"
          ? view.triggerKey
          : row.target === "rewriteKey"
            ? view.rewriteKey
            : view.pasteAgain;
    const capturing = capture.target === row.target;
    controls[row.id] = (
      <KeyControl
        keys={keys}
        capturing={capturing}
        prompt={row.prompt}
        name={row.name}
        onChange={() => capture.start(row.target)}
        onCancel={capture.cancel}
        {...(row.target === "triggerKey" ? { onRemove: () => void bridge.call("settings.clearTriggerKey", {}) } : {})}
        {...(row.target === "rewriteKey" ? { onRemove: () => void bridge.call("settings.clearRewriteKey", {}) } : {})}
      />
    );
    if (capturing && capture.hint) notes[row.id] = capture.hint;
    else if (!capturing) {
      // Keys that are also a Windows shortcut on this PC: a notice, never a block (VED-74).
      const conflict = row.target === "triggerKey" ? view.triggerKeyConflict : row.target === "rewriteKey" ? view.rewriteKeyConflict : null;
      if (conflict) notes[row.id] = conflict;
    }
  }

  if (view.pasteAgainTaken && capture.target !== "pasteAgain") {
    notes["paste-again"] = `Another app is using ${view.pasteAgain.join(" + ")}, so Paste Again can't work. Choose other keys.`;
  }

  return { controls, notes };
}
