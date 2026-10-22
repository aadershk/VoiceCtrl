import { useEffect, useRef, useState } from "react";
import type { Bridge } from "../../bridge/bridge";
import type { DictationSettingsView, MicrophonesView } from "../../bridge/contracts.g";
import { Button, Menu, MenuAnchor, MenuItem, MenuSeparator } from "../../design/components";

/** How each of the three bars answers the level: the middle one is the tallest, as in the logo. */
const BAR_GAIN = [0.8, 1.25, 1] as const;
const BAR_REST = 0.3;

/**
 * The microphone row's live three-bar level (research 10: "a live three-bar coral level"). It
 * asks the host for the level only while it is on screen and the window is visible, because an
 * open microphone lights Windows' "in use" indicator. Bars are set straight on the elements:
 * twenty React renders a second of the whole page would be waste.
 */
export function MicLevel({ bridge }: { bridge: Bridge }) {
  const ref = useRef<HTMLSpanElement>(null);

  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    const bars = Array.from(el.children) as HTMLElement[];
    const show = (level: number) =>
      bars.forEach((bar, i) => {
        const scale = Math.min(1, BAR_REST + level * (BAR_GAIN[i] ?? 1));
        bar.style.transform = `scaleY(${scale.toFixed(3)})`;
      });
    show(0);
    const offLevel = bridge.on("mic.level", ({ level }) => show(level));

    let onScreen = false;
    let previewing = false;
    const update = () => {
      const want = onScreen && document.visibilityState === "visible";
      if (want === previewing) return;
      previewing = want;
      if (!want) show(0);
      void bridge.call("mic.preview", { on: want }).catch(() => undefined);
    };
    const observer = typeof IntersectionObserver === "undefined"
      ? null
      : new IntersectionObserver((entries) => {
          onScreen = entries.some((entry) => entry.isIntersecting);
          update();
        });
    observer?.observe(el);
    document.addEventListener("visibilitychange", update);
    return () => {
      offLevel();
      observer?.disconnect();
      document.removeEventListener("visibilitychange", update);
      onScreen = false;
      update();
    };
  }, [bridge]);

  return (
    <span ref={ref} className="hub-mic-level" aria-hidden="true">
      <i />
      <i />
      <i />
    </span>
  );
}

export interface MicrophoneControlProps {
  bridge: Bridge;
  view: DictationSettingsView;
  mics: MicrophonesView;
  /** Opens the menu on arrival: the status card's "No microphone" lands here to fix it. */
  landing: boolean;
  onPick: (id: string | null) => void;
}

export function defaultLabel(mics: MicrophonesView): string {
  return mics.default ? `Windows default · ${mics.default.name}` : "Windows default";
}

/** The dropdown's label: the chosen microphone, or the default it falls back to. */
export function microphoneLabel(view: DictationSettingsView, mics: MicrophonesView): string {
  if (view.microphoneId === null) return defaultLabel(mics);
  const device = mics.devices.find((d) => d.id === view.microphoneId);
  return device?.name ?? view.microphoneName ?? "Microphone";
}

export function isUnplugged(view: DictationSettingsView, mics: MicrophonesView): boolean {
  return view.microphoneId !== null && !mics.devices.some((d) => d.id === view.microphoneId);
}

/** Microphone (research 10 Settings): the live level beside a dropdown of Windows' microphones. */
export function MicrophoneControl({ bridge, view, mics, landing, onPick }: MicrophoneControlProps) {
  const [open, setOpen] = useState(false);
  const hasMics = mics.devices.length > 0;

  useEffect(() => {
    if (landing && hasMics) setOpen(true);
  }, [landing, hasMics]);

  if (!hasMics) {
    return (
      <Button className="hub-mic-button" disabled>
        No microphone found
      </Button>
    );
  }

  const unplugged = isUnplugged(view, mics);
  // The default is its own item, so it isn't listed twice, unless it was picked by name.
  const others = mics.devices.filter((d) => d.id !== mics.default?.id || d.id === view.microphoneId);

  return (
    <>
      <MicLevel bridge={bridge} />
      <MenuAnchor>
        <Button
          className="hub-mic-button"
          trailingIcon="chevronDown"
          aria-haspopup="menu"
          aria-expanded={open}
          aria-label={`Microphone: ${microphoneLabel(view, mics)}`}
          title={microphoneLabel(view, mics)}
          onClick={() => setOpen((o) => !o)}
        >
          <span className="hub-mic-label">{microphoneLabel(view, mics)}</span>
        </Button>
        <Menu open={open} onClose={() => setOpen(false)} label="Microphone" placement="below-end">
          <MenuItem selected={view.microphoneId === null} onSelect={() => onPick(null)}>
            {defaultLabel(mics)}
          </MenuItem>
          {others.length > 0 && <MenuSeparator />}
          {others.map((device) => (
            <MenuItem key={device.id} selected={device.id === view.microphoneId} onSelect={() => onPick(device.id)}>
              {device.name}
            </MenuItem>
          ))}
          {unplugged && (
            <MenuItem selected disabled onSelect={() => undefined}>
              {view.microphoneName ?? "Microphone"} · not connected
            </MenuItem>
          )}
        </Menu>
      </MenuAnchor>
    </>
  );
}
