import { useEffect, useLayoutEffect, useReducer, useRef, useState } from "react";
import type { Bridge } from "../bridge/bridge";
import type { AppSnapshot, SetupSnapshot } from "../bridge/contracts.g";
import { Icon, Logo, WindowControls } from "../design/components";
import { cx } from "../design/cx";
import { cascade } from "../design/motion";
import { initialState, railIndex, setupReducer, stepTwoLabel } from "./flow";
import type { SetupState } from "./flow";
import { KeyStep, ModeStep, ModelStep, ReadyStep } from "./steps";
import { KEY_PAGES } from "../hub/settings/providers";

/** Google AI Studio's key page, the default provider's; the host opens it in the default browser
 * (allowlisted host, as are NVIDIA's and OpenAI's in KEY_PAGES). */
export const AI_STUDIO_URL = KEY_PAGES.gemini!.url;

/** "The key works" shows for this long before Setup moves on by itself (research 13). */
export const KEY_WORKS_PAUSE_MS = 700;

export interface SetupProps {
  bridge: Bridge;
  app: AppSnapshot;
  setup: SetupSnapshot;
}

/**
 * First-run Setup: the split window of the approved C · Split board. The left rail shows the
 * three steps; the raised panel on the right holds the current one.
 */
export function Setup({ bridge, app, setup }: SetupProps) {
  const [state, dispatch] = useReducer(setupReducer, setup, initialState);
  const [appState, setAppState] = useState(app);
  const viewRef = useRef<HTMLDivElement>(null);
  const autoAdvance = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  useEffect(() => {
    const offModel = bridge.on("setup.model", ({ status }) => dispatch({ type: "model", status }));
    const offTheme = bridge.on("app.theme", ({ theme, themePreference }) =>
      setAppState((s) => ({ ...s, theme, themePreference })),
    );
    const offMotion = bridge.on("app.reducedMotion", ({ reducedMotion }) => setAppState((s) => ({ ...s, reducedMotion })));
    return () => {
      offModel();
      offTheme();
      offMotion();
      clearTimeout(autoAdvance.current);
    };
  }, [bridge]);

  // The host also sets these before first paint; keeping them current here covers live changes.
  useEffect(() => {
    document.documentElement.dataset.theme = appState.theme;
    document.documentElement.dataset.reducedMotion = String(appState.reducedMotion);
  }, [appState.theme, appState.reducedMotion]);

  // A step change runs the Glide enter through the step's blocks, then puts focus on its title
  // so a screen reader announces the new step. Nothing animates on first paint.
  const firstStep = useRef(true);
  useLayoutEffect(() => {
    const view = viewRef.current;
    if (!view) return;
    if (firstStep.current) {
      firstStep.current = false;
      return;
    }
    cascade(Array.from(view.children));
    view.querySelector<HTMLElement>("h1")?.focus({ preventScroll: true });
  }, [state.step]);

  const call = bridge.call.bind(bridge);
  const startDownload = (s: SetupState) => {
    if (s.model.state !== "done") void call("setup.downloadModel", {}).catch(() => undefined);
  };
  const stopDownload = () => void call("setup.stopDownload", {}).catch(() => undefined);

  const actions = {
    pick: (listening: SetupState["listening"]) => dispatch({ type: "pick", listening }),
    continueFromMode: () => {
      dispatch({ type: "continueFromMode" });
      if (state.listening === "offline") startDownload(state);
    },
    editKey: (key: string) => {
      clearTimeout(autoAdvance.current);
      dispatch({ type: "editKey", key });
    },
    openKeyPage: () => {
      const page = KEY_PAGES[state.provider];
      if (!page) return;
      window.open(page.url, "_blank", "noopener");
      dispatch({ type: "openedAiStudio" });
    },
    pickProvider: (provider: SetupState["provider"]) => {
      clearTimeout(autoAdvance.current);
      dispatch({ type: "pickProvider", provider });
    },
    editOther: (baseUrl: string, model: string) => {
      clearTimeout(autoAdvance.current);
      dispatch({ type: "editOther", baseUrl, model });
    },
    submitKey: async () => {
      if (state.keyState === "didNotWork" || state.keyState === "notChecked") {
        dispatch({ type: "useKey" });
        return;
      }
      const key = state.key;
      dispatch({ type: "checkingKey" });
      let check: "works" | "didNotWork" | "notChecked";
      try {
        ({ check } = await call("setup.checkKey", { key, ...providerArgs(state) }));
      } catch {
        // A check that can't run never blocks Setup (research 13).
        check = "notChecked";
      }
      dispatch({ type: "keyChecked", key, check });
      if (check === "works") {
        autoAdvance.current = setTimeout(() => dispatch({ type: "useKey" }), KEY_WORKS_PAUSE_MS);
      }
    },
    skip: () => {
      clearTimeout(autoAdvance.current);
      if (state.step === "model") stopDownload();
      dispatch({ type: "skip" });
    },
    back: () => {
      clearTimeout(autoAdvance.current);
      if (state.step === "model") stopDownload();
      if (state.step === "ready" && state.listening === "offline") startDownload(state);
      dispatch({ type: "back" });
    },
    retryDownload: () => startDownload(state),
    continueFromModel: () => dispatch({ type: "continueFromModel" }),
    setStartWithWindows: (on: boolean) => dispatch({ type: "startWithWindows", on }),
    setSmartCleanup: (on: boolean) => dispatch({ type: "smartCleanup", on }),
    finish: async () => {
      dispatch({ type: "finishing" });
      try {
        await call("setup.finish", {
          listening: state.listening,
          apiKey: state.listening === "online" ? state.usedKey : null,
          startWithWindows: state.startWithWindows,
          smartCleanup: state.listening === "offline" || state.smartCleanup,
          ...providerArgs(state),
        });
        // The host closes this window and opens the Hub.
      } catch {
        dispatch({ type: "finishFailed" });
      }
    },
  };

  return (
    <div className="setup">
      <div className="setup-drag" aria-hidden="true" />
      <WindowControls
        onMinimize={() => void call("window.minimize", {})}
        onClose={() => void call("window.close", {})}
      />
      <Rail state={state} version={appState.version} />
      <main className="setup-panel">
        <div className="setup-view" ref={viewRef} key={state.step}>
          {state.step === "mode" && <ModeStep state={state} actions={actions} />}
          {state.step === "key" && <KeyStep state={state} actions={actions} />}
          {state.step === "model" && <ModelStep state={state} actions={actions} />}
          {state.step === "ready" && <ReadyStep state={state} actions={actions} bridge={bridge} />}
        </div>
      </main>
    </div>
  );
}

export type SetupActions = {
  pick: (listening: SetupState["listening"]) => void;
  continueFromMode: () => void;
  editKey: (key: string) => void;
  openKeyPage: () => void;
  pickProvider: (provider: SetupState["provider"]) => void;
  editOther: (baseUrl: string, model: string) => void;
  submitKey: () => Promise<void>;
  skip: () => void;
  back: () => void;
  retryDownload: () => void;
  continueFromModel: () => void;
  setStartWithWindows: (on: boolean) => void;
  setSmartCleanup: (on: boolean) => void;
  finish: () => Promise<void>;
};

/** The provider, and Other's address and model (null for the presets), as the host takes them. */
function providerArgs(state: SetupState) {
  const other = state.provider === "other";
  return {
    provider: state.provider,
    baseUrl: other ? state.otherBaseUrl.trim() : null,
    model: other ? state.otherModel.trim() : null,
  };
}

function Rail({ state, version }: { state: SetupState; version: string }) {
  const now = railIndex(state.step);
  const steps = [
    { n: 1, label: "Choose how it listens" },
    { n: 2, label: stepTwoLabel(state.listening) },
    { n: 3, label: "Learn your hotkey" },
  ];
  return (
    <aside className="setup-rail">
      <div className="setup-brand">
        <Logo size={28} />
        <span className="vc-brand">VoiceCtrl</span>
      </div>
      <p className="setup-line">
        <span>Say it once.</span>
        <span>It lands where you type.</span>
      </p>
      <ol className="setup-steps" aria-label="Setup steps">
        {steps.map(({ n, label }) => {
          const done = n < now;
          return (
            <li
              key={n}
              className={cx("setup-rail-step", done && "is-done", n === now && "is-now")}
              aria-current={n === now ? "step" : undefined}
            >
              <span className="setup-rail-num" aria-hidden="true">
                {done ? <Icon name="check" size={14} /> : n}
              </span>
              {label}
            </li>
          );
        })}
      </ol>
      <span className="setup-rail-foot">Free and open source · {version}</span>
    </aside>
  );
}
