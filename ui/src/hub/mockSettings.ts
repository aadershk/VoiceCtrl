/**
 * The Settings page's fake host commands (spec §6), for `npm run dev`, Vitest and the Playwright
 * visual tests. "Press a key" listens to the page's own keydown here; in the app the host's
 * keyboard hook does it, so Win and Alt work and nothing reaches other apps.
 */
import type { MockHost } from "../bridge/mock";
import type { DictationSettingsView, KeyCaptureTarget, MicrophoneDevice, MicrophonesView } from "../bridge/contracts.g";

export const MOCK_MICS: readonly MicrophoneDevice[] = [
  { id: "{realtek}", name: "Microphone (Realtek Audio)" },
  { id: "{jabra}", name: "Headset (Jabra Evolve2 65)" },
  { id: "{yeti}", name: "USB Microphone (Yeti)" },
];

export interface SettingsScenario {
  /** "none": no microphones. "unplugged": the chosen Yeti isn't connected. */
  mics?: "some" | "none" | "unplugged";
  /** The chosen microphone's id; default the Jabra, as on the board. */
  microphoneId?: string | null;
  hotkey?: string;
  /** Trigger Key chips, e.g. ["F9"]; Left Ctrl + Left Win by default, null once removed. */
  triggerKey?: readonly string[] | null;
  /** Rewrite key chips; Left Ctrl + Left Shift by default, null once removed. */
  rewriteKey?: readonly string[] | null;
  pasteAgainTaken?: boolean;
  /** Several keyboard layouts installed, so Ctrl + Shift also switches the language (VED-74). */
  severalLanguages?: boolean;
  pressEnter?: boolean;
  muteOtherAudio?: boolean;
  /** Leaves the level still, for screenshots. */
  level?: number;
}

/** Reads a scenario from the page URL, e.g. `hub.html?screen=settings&mics=unplugged&trigger=F9`. */
export function settingsScenarioFromQuery(search: string): SettingsScenario {
  const q = new URLSearchParams(search);
  const scenario: SettingsScenario = {};
  const mics = q.get("mics");
  if (mics === "none" || mics === "unplugged") scenario.mics = mics;
  const trigger = q.get("trigger");
  if (trigger === "none") scenario.triggerKey = null;
  else if (trigger) scenario.triggerKey = trigger.split("+");
  if (q.get("pasteTaken") === "1") scenario.pasteAgainTaken = true;
  if (q.get("languages") === "many") scenario.severalLanguages = true;
  if (q.get("enter") === "1") scenario.pressEnter = true;
  if (q.get("mute") === "0") scenario.muteOtherAudio = false;
  const level = q.get("level");
  if (level) scenario.level = Number(level);
  return scenario;
}

// The page's KeyboardEvent.code, as the host's hook would name the key.
const CODE_NAMES: Record<string, string> = {
  ControlLeft: "Left Ctrl",
  ControlRight: "Right Ctrl",
  ShiftLeft: "Left Shift",
  ShiftRight: "Right Shift",
  AltLeft: "Left Alt",
  AltRight: "Right Alt",
  MetaLeft: "Left Win",
  MetaRight: "Right Win",
  CapsLock: "Caps Lock",
  Space: "Space",
};

function keyName(event: KeyboardEvent): string {
  return CODE_NAMES[event.code] ?? (event.key.length === 1 ? event.key.toUpperCase() : event.key);
}

export function installSettingsMock(host: MockHost, scenario: SettingsScenario = {}): void {
  const mode = scenario.mics ?? "some";
  const devices = mode === "none" ? [] : mode === "unplugged" ? MOCK_MICS.slice(0, 2) : MOCK_MICS;
  const microphones: MicrophonesView = { devices: [...devices], default: devices[0] ?? null };
  const chosen = mode === "unplugged" ? "{yeti}" : scenario.microphoneId === undefined ? "{jabra}" : scenario.microphoneId;

  let view: DictationSettingsView = {
    microphoneId: mode === "none" ? null : chosen,
    microphoneName: MOCK_MICS.find((m) => m.id === chosen)?.name ?? null,
    hotkey: [scenario.hotkey ?? "Ctrl"],
    triggerKey: scenario.triggerKey === undefined ? ["Left Ctrl", "Left Win"] : scenario.triggerKey ? [...scenario.triggerKey] : null,
    rewriteKey: scenario.rewriteKey === undefined ? ["Left Ctrl", "Left Shift"] : scenario.rewriteKey ? [...scenario.rewriteKey] : null,
    pasteAgain: ["Ctrl", "Shift", "V"],
    pasteAgainTaken: scenario.pasteAgainTaken ?? false,
    pressEnter: scenario.pressEnter ?? false,
    muteOtherAudio: scenario.muteOtherAudio ?? true,
    triggerKeyConflict: null,
    rewriteKeyConflict: null,
  };
  // Core's HotkeyConflicts, for the one case the page can show: Ctrl + Shift with several layouts.
  const withConflicts = (next: DictationSettingsView): DictationSettingsView => {
    const conflict = (keys: readonly string[] | null) =>
      scenario.severalLanguages && keys && keys.length === 2 && keys.some((k) => k.endsWith("Ctrl")) && keys.some((k) => k.endsWith("Shift"))
        ? `${keys.join(" + ")} also switches your keyboard language. Turn that off in Windows settings or pick other keys.`
        : null;
    return { ...next, triggerKeyConflict: conflict(next.triggerKey), rewriteKeyConflict: conflict(next.rewriteKey) };
  };
  view = withConflicts(view);
  const save = (patch: Partial<DictationSettingsView>) => {
    view = withConflicts({ ...view, ...patch });
    host.emit("settings.dictation", { dictation: view });
  };

  let stopCapture: (() => void) | null = null;
  let levelTimer: ReturnType<typeof setInterval> | undefined;

  host.handle("settings.getDictation", () => view);
  host.handle("mic.list", () => microphones);

  host.handle("settings.setMicrophone", ({ id }) => {
    save({ microphoneId: id, microphoneName: MOCK_MICS.find((m) => m.id === id)?.name ?? null });
    return null;
  });

  host.handle("settings.setPressEnter", ({ on }) => {
    save({ pressEnter: on });
    return null;
  });

  host.handle("settings.setMuteOtherAudio", ({ on }) => {
    save({ muteOtherAudio: on });
    return null;
  });

  host.handle("settings.clearTriggerKey", () => {
    save({ triggerKey: null });
    return null;
  });

  host.handle("settings.clearRewriteKey", () => {
    save({ rewriteKey: null });
    return null;
  });

  host.handle("settings.captureKey", ({ target }) => {
    stopCapture?.();
    return new Promise((resolve) => {
      const held: string[] = [];
      const finish = (saved: boolean, warning: string | null = null) => {
        window.removeEventListener("keydown", onKeyDown, true);
        window.removeEventListener("keyup", onKeyUp, true);
        stopCapture = null;
        resolve({ saved, warning });
      };
      const onKeyDown = (event: KeyboardEvent) => {
        event.preventDefault();
        if (event.key === "Escape") return finish(false);
        const name = keyName(event);
        if (!held.includes(name)) held.push(name);
      };
      const onKeyUp = (event: KeyboardEvent) => {
        event.preventDefault();
        if (held.length === 0) return;
        const keys = [...held];
        held.length = 0;
        const accepted = acceptKeys(target, keys);
        // Core refuses the other hold's keys, in any order.
        const other = target === "triggerKey" ? view.rewriteKey : target === "rewriteKey" ? view.triggerKey : null;
        if (typeof accepted !== "string" && other && other.length === accepted.length && accepted.every((k) => other.includes(k))) {
          host.emit("settings.captureHint", {
            message: target === "triggerKey" ? "That's your Rewrite key. Choose another key." : "That's your Trigger Key. Choose another key.",
          });
          return;
        }
        if (typeof accepted === "string") {
          host.emit("settings.captureHint", { message: accepted });
          return;
        }
        save(
          target === "hotkey"
            ? { hotkey: accepted }
            : target === "triggerKey"
              ? { triggerKey: accepted }
              : target === "rewriteKey"
                ? { rewriteKey: accepted }
                : { pasteAgain: accepted, pasteAgainTaken: false },
        );
        finish(true, languageSwitchWarning(target, accepted));
      };
      window.addEventListener("keydown", onKeyDown, true);
      window.addEventListener("keyup", onKeyUp, true);
      stopCapture = () => finish(false);
    });
  });

  host.handle("settings.cancelCapture", () => {
    stopCapture?.();
    return null;
  });

  host.handle("mic.preview", ({ on }) => {
    clearInterval(levelTimer);
    if (!on || devices.length === 0) return null;
    if (scenario.level !== undefined) {
      host.emit("mic.level", { level: scenario.level });
      return null;
    }
    let t = 0;
    levelTimer = setInterval(() => {
      t += 1;
      host.emit("mic.level", { level: Math.abs(Math.sin(t / 4)) * 0.8 });
    }, 50);
    return null;
  });
}

const LANGUAGE_SWITCH_WARNING = "On setups with several keyboard layouts, Windows may switch the input language.";

/** Core warns when a two-key Ctrl + Shift is saved for either hold. */
function languageSwitchWarning(target: KeyCaptureTarget, keys: string[]): string | null {
  const isCtrlShift = keys.length === 2 && keys.some((k) => k.endsWith("Ctrl")) && keys.some((k) => k.endsWith("Shift"));
  return isCtrlShift && (target === "triggerKey" || target === "rewriteKey") ? LANGUAGE_SWITCH_WARNING : null;
}

const MODIFIERS = new Set(["Left Ctrl", "Right Ctrl", "Left Shift", "Right Shift", "Left Alt", "Right Alt", "Left Win", "Right Win"]);

/** A rough stand-in for Core's KeyCapture rules: chips when usable, a hint when not. */
function acceptKeys(target: KeyCaptureTarget, keys: string[]): string[] | string {
  if (target === "pasteAgain") {
    const mods = keys.filter((k) => MODIFIERS.has(k)).map((k) => k.split(" ")[1] ?? k);
    const rest = keys.filter((k) => !MODIFIERS.has(k));
    if (rest.length !== 1 || mods.length === 0 || mods.every((m) => m === "Shift")) return "Hold Ctrl, Alt or Win and press a key.";
    return [...new Set(mods)].concat(rest);
  }
  if (keys.length === 1 && (MODIFIERS.has(keys[0]!) || /^F\d+$/.test(keys[0]!) || keys[0] === "Caps Lock")) {
    return target === "hotkey" && keys[0]!.endsWith("Ctrl") ? ["Ctrl"] : keys;
  }
  if ((target === "triggerKey" || target === "rewriteKey") && keys.length >= 2 && keys.length <= 3 && keys.some((k) => MODIFIERS.has(k)) && keys.filter((k) => !MODIFIERS.has(k)).length <= 1) {
    // Core refuses Win + L and Win + D: Windows acts on them at key-down and can't be stopped.
    const win = keys.some((k) => k.endsWith("Win"));
    if (win && keys.some((k) => k === "L" || k === "D")) return "Windows uses that combination itself. Choose another.";
    return keys;
  }
  return target === "hotkey"
    ? "Choose a key you don't type with, like Ctrl, Caps Lock or F8."
    : "Press one key like F8, or hold Ctrl, Alt, Shift or Win and press another key.";
}
