/**
 * The Setup flow (research 13, spec §7) as a pure reducer: Choose how it listens → key or model →
 * You're all set. The page calls the host for the side effects (key check, download, finish);
 * this decides only what shows.
 */
import type { ModelDownloadStatus, OnlineProvider, SetupKeyCheck, SetupListening, SetupSnapshot } from "../bridge/contracts.g";

export type Step = "mode" | "key" | "model" | "ready";

export type KeyState = "idle" | "checking" | SetupKeyCheck;

export interface SetupState {
  step: Step;
  listening: SetupListening;
  /** The Online provider (ticket 60): Gemini unless they pick another. */
  provider: OnlineProvider;
  /** Other's address and model; ignored for the presets. */
  otherBaseUrl: string;
  otherModel: string;
  key: string;
  keyState: KeyState;
  aiStudioOpened: boolean;
  model: ModelDownloadStatus;
  hotkey: string;
  startWithWindows: boolean;
  /** Online listeners' answer to "also work without internet" (the Smart cleanup model). Offline always has it. */
  smartCleanup: boolean;
  /** Which step 2 was skipped, for the note on the last step. */
  skipped: "key" | "model" | null;
  /** The key Setup finishes with: checked, or used anyway. Null when skipped. */
  usedKey: string | null;
  finishing: boolean;
}

export type SetupAction =
  | { type: "pick"; listening: SetupListening }
  | { type: "continueFromMode" }
  | { type: "editKey"; key: string }
  | { type: "pickProvider"; provider: OnlineProvider }
  | { type: "editOther"; baseUrl: string; model: string }
  | { type: "openedAiStudio" }
  | { type: "checkingKey" }
  | { type: "keyChecked"; key: string; check: SetupKeyCheck }
  /** Continue with the key: after "The key works" (automatic), or Use it anyway. */
  | { type: "useKey" }
  | { type: "skip" }
  | { type: "back" }
  | { type: "model"; status: ModelDownloadStatus }
  | { type: "continueFromModel" }
  | { type: "startWithWindows"; on: boolean }
  | { type: "smartCleanup"; on: boolean }
  | { type: "finishing" }
  | { type: "finishFailed" };

export function initialState(snapshot: SetupSnapshot): SetupState {
  return {
    step: "mode",
    listening: "online",
    provider: "gemini",
    otherBaseUrl: "",
    otherModel: "",
    key: "",
    keyState: "idle",
    aiStudioOpened: false,
    model: snapshot.model,
    hotkey: snapshot.hotkey,
    startWithWindows: snapshot.startWithWindows,
    smartCleanup: true,
    skipped: null,
    usedKey: null,
    finishing: false,
  };
}

/** The primary button on the key step: disabled while empty, checking, or about to move on. */
export function keyPrimary(state: SetupState): { label: "Continue" | "Use it anyway"; disabled: boolean } {
  const anyway = state.keyState === "didNotWork" || state.keyState === "notChecked";
  // Other needs its address and model before a key can be checked.
  const otherIncomplete = state.provider === "other" && (state.otherBaseUrl.trim() === "" || state.otherModel.trim() === "");
  return {
    label: anyway ? "Use it anyway" : "Continue",
    disabled: state.key.trim() === "" || otherIncomplete || state.keyState === "checking" || state.keyState === "works",
  };
}

/** Step 2's rail label follows the choice. */
export function stepTwoLabel(listening: SetupListening): string {
  return listening === "offline" ? "Get the offline model" : "Connect your Online provider";
}

export function railIndex(step: Step): 1 | 2 | 3 {
  return step === "mode" ? 1 : step === "ready" ? 3 : 2;
}

export function setupReducer(state: SetupState, action: SetupAction): SetupState {
  switch (action.type) {
    case "pick":
      return state.step === "mode" ? { ...state, listening: action.listening } : state;

    case "continueFromMode":
      return { ...state, step: state.listening === "online" ? "key" : "model" };

    case "editKey":
      return { ...state, key: action.key, keyState: "idle" };

    case "pickProvider":
      // Another provider's key is another key: the field starts over.
      return state.provider === action.provider || state.keyState === "checking"
        ? state
        : { ...state, provider: action.provider, key: "", keyState: "idle", aiStudioOpened: false };

    case "editOther":
      return { ...state, otherBaseUrl: action.baseUrl, otherModel: action.model, keyState: "idle" };

    case "openedAiStudio":
      return { ...state, aiStudioOpened: true };

    case "checkingKey":
      return { ...state, keyState: "checking" };

    case "keyChecked":
      // A check that comes back after the user edited the key, went Back or skipped is stale.
      if (state.step !== "key" || state.keyState !== "checking" || state.key !== action.key) return state;
      return { ...state, keyState: action.check };

    case "useKey":
      if (state.step !== "key") return state;
      if (state.keyState !== "works" && state.keyState !== "didNotWork" && state.keyState !== "notChecked") return state;
      return { ...state, step: "ready", skipped: null, usedKey: state.key.trim() };

    case "skip":
      if (state.step === "key") return { ...state, step: "ready", skipped: "key", usedKey: null, keyState: "idle" };
      if (state.step === "model") return { ...state, step: "ready", skipped: state.model.state === "done" ? null : "model" };
      return state;

    case "back":
      switch (state.step) {
        case "key":
        case "model":
          return { ...state, step: "mode", keyState: state.keyState === "checking" ? "idle" : state.keyState };
        case "ready":
          return {
            ...state,
            step: state.listening === "online" ? "key" : "model",
            // A key that worked is checked again on Continue; a failed state stays visible.
            keyState: state.keyState === "works" ? "idle" : state.keyState,
          };
        default:
          return state;
      }

    case "model":
      return { ...state, model: action.status };

    case "continueFromModel":
      return state.step === "model" && state.model.state === "done" ? { ...state, step: "ready", skipped: null } : state;

    case "startWithWindows":
      return { ...state, startWithWindows: action.on };

    case "smartCleanup":
      return { ...state, smartCleanup: action.on };

    case "finishing":
      return { ...state, finishing: true };

    case "finishFailed":
      return { ...state, finishing: false };
  }
}

/** The note on the last step when step 2 was skipped. */
export function skipNote(state: SetupState): string | null {
  if (state.skipped === "key") {
    return "No key yet, so VoiceCtrl works offline until you add one in Settings. The offline model downloads on your first Dictation.";
  }
  if (state.skipped === "model") return "The offline model downloads the first time you dictate.";
  return null;
}
