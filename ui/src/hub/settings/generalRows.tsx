import { useEffect, useState } from "react";
import type { ReactNode } from "react";
import type { KeepDictations, OverlayDock } from "../../bridge/contracts.g";
import { Button, Segmented, Switch } from "../../design/components";
import { useHub } from "../hubContext";
import { CopyDiagnosticsButton } from "./CopyDiagnosticsButton";
import type { GroupRows } from "./dictationRows";
import { UpdatesControl } from "./UpdatesControl";
import { updateLine } from "./updates";
import { useGeneralSettings } from "./useGeneralSettings";

const dockOptions = [
  { value: "bottom", label: "Bottom" },
  { value: "left", label: "Left" },
  { value: "right", label: "Right" },
] as const satisfies readonly { value: OverlayDock; label: string }[];

const keepOptions = [
  { value: "last50", label: "Last 50" },
  { value: "last24Hours", label: "24 hours" },
  { value: "none", label: "None" },
] as const satisfies readonly { value: KeepDictations; label: string }[];

const dictations = (n: number) => (n === 1 ? "1 Dictation" : `${n} Dictations`);

// The update row's "Checked today at…" moves on by itself past midnight; a minute is plenty.
const CLOCK_TICK_MS = 60_000;

/**
 * Sound and Overlay, Privacy and App (slice 23). Every change is saved by the host and applies at
 * once; the rows follow `settings.general`. Mute Other Audio's row comes from `useDictationRows`,
 * since the setting is the Dictation section's.
 */
export function useGeneralRows(): GroupRows {
  const { bridge, confirm, toast } = useHub();
  const view = useGeneralSettings(bridge);
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), CLOCK_TICK_MS);
    return () => clearInterval(timer);
  }, []);

  if (!view) return { controls: {}, notes: {}, descriptions: {} };

  // A shorter choice deletes kept Dictations at once, on disk, so it says how many first. The one
  // confirm besides Clear all, because it is a Clear all when it removes everything.
  const pickKeep = async (keep: KeepDictations) => {
    const { removes } = await bridge.call("settings.previewKeepDictations", { keep });
    if (removes > 0) {
      const confirmed = await confirm(
        keep === "none"
          ? {
              title: "Stop keeping Dictations?",
              body: `The ${dictations(removes)} kept on this PC are removed now. Paste Again still pastes your latest Transcription.`,
              confirmLabel: `Remove ${removes}`,
            }
          : {
              title: "Keep only the last 24 hours?",
              body: `The ${dictations(removes)} older than a day are removed from this PC now.`,
              confirmLabel: `Remove ${removes}`,
            },
      );
      if (!confirmed) return;
    }
    await bridge.call("settings.setKeepDictations", { keep });
  };

  const openFolder = async () => {
    try {
      await bridge.call("settings.openDataFolder", {});
    } catch {
      toast("Couldn't open the folder. Try again.", { error: true });
    }
  };

  const controls: Record<string, ReactNode> = {
    "sound-cues": (
      <div className="hub-control-pair">
        <Button variant="ghost" size="sm" icon="play" onClick={() => void bridge.call("settings.playSoundCues", {})}>
          Play
        </Button>
        <Switch label="Sound Cues" checked={view.soundCues} onChange={(on) => void bridge.call("settings.setSoundCues", { on })} />
      </div>
    ),
    "overlay-position": (
      <Segmented
        label="Overlay position"
        options={dockOptions}
        value={view.overlayDock}
        onChange={(dock) => void bridge.call("settings.setOverlayDock", { dock })}
      />
    ),
    "always-show-overlay": (
      <Switch
        label="Always show the Overlay"
        checked={view.alwaysShowOverlay}
        onChange={(on) => void bridge.call("settings.setAlwaysShowOverlay", { on })}
      />
    ),
    "keep-dictations": (
      <Segmented label="Keep Dictations" options={keepOptions} value={view.keepDictations} onChange={(keep) => void pickKeep(keep)} />
    ),
    "your-data": (
      <Button size="sm" trailingIcon="external" onClick={() => void openFolder()}>
        Open folder
      </Button>
    ),
    "copy-diagnostics": <CopyDiagnosticsButton />,
    "engine-record": (
      <Switch label="Engine record" checked={view.engineRecord} onChange={(on) => void bridge.call("settings.setEngineRecord", { on })} />
    ),
    "start-with-windows": (
      <Switch
        label="Start VoiceCtrl with Windows"
        checked={view.startWithWindows}
        onChange={(on) => void bridge.call("settings.setStartWithWindows", { on })}
      />
    ),
    "automatic-updates": (
      <UpdatesControl
        view={view.updates}
        onCheck={() => void bridge.call("updates.check", {})}
        onDownload={() => void bridge.call("updates.download", {})}
        onRestart={() => void bridge.call("updates.restart", {})}
        onAutomatic={(on) => void bridge.call("updates.setAutomatic", { on })}
      />
    ),
  };

  const descriptions: Record<string, ReactNode> = {
    "automatic-updates": <span aria-live="polite">{updateLine(view.updates, now)}</span>,
  };

  return { controls, notes: {}, descriptions };
}
