import type { KeyboardEvent } from "react";
import type { Bridge } from "../bridge/bridge";
import type { SetupListening } from "../bridge/contracts.g";
import { Button, Icon, ProgressBar, RadioCards, Segmented, Spinner, Switch, TextField } from "../design/components";
import type { RadioCardOption } from "../design/components";
import { KEY_HINTS, KEY_PAGES, PROVIDERS } from "../hub/settings/providers";
import { keyPrimary, skipNote } from "./flow";
import type { SetupState } from "./flow";
import { downloadLine, downloadPercent } from "./format";
import type { SetupActions } from "./Setup";
import { HotkeyRows } from "./HotkeyRows";

interface StepProps {
  state: SetupState;
  actions: SetupActions;
}

const listeningOptions = [
  {
    value: "online",
    label: "Online",
    badge: "Recommended",
    icon: "cloud",
    description: "Best accuracy, using the AI provider you pick: Gemini, NVIDIA, OpenAI or another. Needs an API key and an internet connection.",
  },
  {
    value: "offline",
    label: "Offline",
    icon: "chip",
    description: "Runs entirely on this PC, so nothing ever leaves it. A one-time 700 MB download.",
  },
] as const satisfies readonly RadioCardOption<SetupListening>[];

/** Step 1 · Choose how it listens. Radio cards plus Continue, so a mis-click never starts a download. */
export function ModeStep({ state, actions }: StepProps) {
  return (
    <>
      <header className="setup-head setup-head-welcome">
        <h1 className="vc-step-title" tabIndex={-1}>Welcome to VoiceCtrl</h1>
        <p className="setup-lede">Speak anywhere in Windows, and VoiceCtrl types it where your cursor is.</p>
      </header>
      <div className="setup-choice">
        <p className="setup-label" aria-hidden="true">How should it turn your speech into text?</p>
        <RadioCards
          label="How should it turn your speech into text?"
          options={listeningOptions}
          value={state.listening}
          onChange={actions.pick}
        />
      </div>
      <footer className="setup-foot">
        <span className="setup-cap setup-grow">You can change this any time in Settings.</span>
        <Button variant="primary" trailingIcon="arrowRight" onClick={actions.continueFromMode}>
          Continue
        </Button>
      </footer>
    </>
  );
}

/** Step 2a · Choose your Online provider and connect its key (ticket 60). */
export function KeyStep({ state, actions }: StepProps) {
  const primary = keyPrimary(state);
  const page = KEY_PAGES[state.provider];
  const submitOnEnter = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "Enter" && !primary.disabled) {
      event.preventDefault();
      void actions.submitKey();
    }
  };
  return (
    <>
      <header className="setup-head">
        <h1 className="vc-step-title" tabIndex={-1}>Choose your Online provider</h1>
        <p className="setup-sub">Your audio and text go only to the provider you pick. Gemini and NVIDIA give a free API key.</p>
      </header>
      <section className="vc-card setup-key-card" aria-label="Choose a provider and paste your key">
        <Segmented label="Online provider" options={PROVIDERS} value={state.provider} onChange={actions.pickProvider} />
        <div className="setup-sep" />
        {page ? (
          <div className="setup-num-row">
            <span className="setup-num" aria-hidden="true">1</span>
            <span className="setup-num-text">
              <b>Get a key</b>
              <span>{page.steps}</span>
            </span>
            {state.aiStudioOpened ? (
              <Button variant="ghost" size="sm" icon="check" onClick={actions.openKeyPage}>
                Opened in your browser
              </Button>
            ) : (
              <Button size="sm" trailingIcon="external" onClick={actions.openKeyPage}>
                {`Open ${page.site}`}
              </Button>
            )}
          </div>
        ) : (
          <div className="setup-num-row setup-num-row-top">
            <span className="setup-num" aria-hidden="true">1</span>
            <div className="setup-key-field setup-other-fields">
              <TextField
                label="Address"
                placeholder="https://api.example.com/v1"
                autoComplete="off"
                spellCheck={false}
                value={state.otherBaseUrl}
                onChange={(event) => actions.editOther(event.target.value, state.otherModel)}
              />
              <TextField
                label="Model"
                placeholder="The model's name"
                autoComplete="off"
                spellCheck={false}
                value={state.otherModel}
                onChange={(event) => actions.editOther(state.otherBaseUrl, event.target.value)}
              />
            </div>
          </div>
        )}
        <div className="setup-sep" />
        <div className="setup-num-row setup-num-row-top">
          <span className="setup-num" aria-hidden="true">2</span>
          <div className="setup-key-field">
            <TextField
              label="Paste it here"
              type="password"
              placeholder={KEY_HINTS[state.provider]}
              autoComplete="off"
              spellCheck={false}
              value={state.key}
              onChange={(event) => actions.editKey(event.target.value)}
              onKeyDown={submitOnEnter}
              error={
                state.keyState === "didNotWork"
                  ? page
                    ? `That key didn't work. Check it on ${page.site} and paste it again, or use it anyway.`
                    : "That key, address or model didn't work. Check them and try again, or use it anyway."
                  : undefined
              }
            />
            <div className="setup-key-status" role="status">
              {state.keyState === "checking" && (
                <span className="setup-note">
                  <Spinner />
                  Checking the key
                </span>
              )}
              {state.keyState === "works" && (
                <span className="setup-ok">
                  <Icon name="check" size={16} />
                  The key works
                </span>
              )}
              {state.keyState === "notChecked" && (
                <span className="setup-note">
                  <Icon name="alert" size={16} />
                  No internet, or the provider is busy, so the key can't be checked now. It's checked on your first Dictation.
                </span>
              )}
            </div>
          </div>
        </div>
      </section>
      <p className="setup-cap">No key yet? Skip for now and add it later in Settings.</p>
      <footer className="setup-foot">
        <Button variant="ghost" onClick={actions.back}>Back</Button>
        <span className="setup-grow" />
        <Button variant="ghost" onClick={actions.skip}>Skip for now</Button>
        <Button variant="primary" disabled={primary.disabled} onClick={() => void actions.submitKey()}>
          {primary.label}
        </Button>
      </footer>
    </>
  );
}
/** Step 2b · Getting the offline model. */
export function ModelStep({ state, actions }: StepProps) {
  const { model } = state;
  const percent = downloadPercent(model);
  const done = model.state === "done";
  return (
    <>
      <header className="setup-head">
        <h1 className="vc-step-title" tabIndex={-1}>Getting the offline model</h1>
        <p className="setup-sub">A one-time download of about 700 MB. After this, VoiceCtrl works with no internet at all.</p>
      </header>
      <section className="vc-card setup-model-card" aria-label="English speech model">
        <div className="setup-model-row">
          <span className="setup-tile" aria-hidden="true">
            <Icon name={done ? "check" : "download"} />
          </span>
          <span className="setup-num-text">
            <b>English speech model</b>
            <span>{downloadLine(model)}</span>
          </span>
          <span className="setup-percent" aria-hidden="true">{percent}%</span>
        </div>
        <ProgressBar value={percent / 100} label="English speech model download" />
        {model.state === "failed" && (
          <div className="setup-failed">
            <span className="setup-error" role="alert">
              <Icon name="alert" size={16} />
              The download stopped. Check your internet connection, then try again.
            </span>
            <Button size="sm" onClick={actions.retryDownload}>Try again</Button>
          </div>
        )}
      </section>
      <p className="setup-cap">
        Smart cleanup, which tidies what you say, is a second download of about 2.3 GB. It starts in the background once you are set
        up. Need other languages? You can add the multilingual model later in Settings.
      </p>
      <footer className="setup-foot">
        <Button variant="ghost" onClick={actions.back}>Back</Button>
        <span className="setup-grow" />
        <Button variant="ghost" onClick={actions.skip}>Skip for now</Button>
        <Button variant="primary" disabled={!done} onClick={actions.continueFromModel}>Continue</Button>
      </footer>
    </>
  );
}

/** Step 3 · You're all set: the two default holds (VED-70) and Start with Windows. */
export function ReadyStep({ state, actions, bridge }: StepProps & { bridge: Bridge }) {
  const note = skipNote(state);
  return (
    <>
      <header className="setup-head">
        <h1 className="vc-step-title" tabIndex={-1}>You're all set</h1>
        <p className="setup-sub">Hold a key to talk in any app. Let go and it types.</p>
      </header>
      <HotkeyRows bridge={bridge} hotkey={state.hotkey} />
      <div className="setup-rows">
        <div className="setup-row">
          <span className="setup-row-label">
            <b>Start VoiceCtrl with Windows</b>
            <span>It waits in the tray, near the clock</span>
          </span>
          <Switch label="Start VoiceCtrl with Windows" checked={state.startWithWindows} onChange={actions.setStartWithWindows} />
        </div>
        {state.listening === "online" && (
          <>
            <div className="setup-sep" />
            <div className="setup-row">
              <span className="setup-row-label">
                <b>Also work without internet</b>
                <span>Smart cleanup model, about 2.3 GB, downloaded in the background</span>
              </span>
              <Switch label="Also work without internet" checked={state.smartCleanup} onChange={actions.setSmartCleanup} />
            </div>
          </>
        )}
      </div>
      {note && (
        <span className="setup-note setup-skip-note">
          <Icon name="alert" size={16} />
          {note}
        </span>
      )}
      <footer className="setup-foot">
        <Button variant="ghost" onClick={actions.back}>Back</Button>
        <span className="setup-grow" />
        <Button variant="primary" trailingIcon="arrowRight" disabled={state.finishing} onClick={() => void actions.finish()}>
          Open VoiceCtrl
        </Button>
      </footer>
    </>
  );
}
