import { useCallback, useEffect, useRef, useState } from "react";
import type { ReactNode } from "react";
import type { OfflinePack, SmartCleanupTier, TranscriptionModePreference } from "../../bridge/contracts.g";
import { Segmented, Switch } from "../../design/components";
import { useHub } from "../hubContext";
import type { GroupRows } from "./dictationRows";
import { ApiKeyControl } from "./ApiKeyControl";
import type { KeyEdit } from "./ApiKeyControl";
import { OfflineModelsControl } from "./OfflineModelsControl";
import { OtherProviderControl } from "./OtherProviderControl";
import { KEY_HINTS, PROVIDERS, PROVIDER_NAMES, keyName } from "./providers";
import { PACK_NAMES, packDownloadLine, packLanguages } from "./packs";
import { SmartCleanupControl } from "./SmartCleanupControl";
import { TIER_HINTS, TIER_NAMES, tierDownloadLine } from "./smartCleanup";
import { useTranscriptionSettings } from "./useTranscriptionSettings";

const modeOptions = [
  { value: "auto", label: "Auto" },
  { value: "online", label: "Online" },
  { value: "offline", label: "Offline" },
] as const satisfies readonly { value: TranscriptionModePreference; label: string }[];

// The stored names, with the friendlier labels people see: verbatim is "Word for word",
// aggressive is "Thorough" (research 10).
const cleanupOptions = [
  { value: "verbatim", label: "Word for word" },
  { value: "light", label: "Light" },
  { value: "standard", label: "Standard" },
  { value: "aggressive", label: "Thorough" },
] as const;

/**
 * The Transcription group (slice 22): Mode, the Online provider and its key (ticket 60: Gemini,
 * NVIDIA, OpenAI or Other, with Other's address and model), Default cleanup and Offline models.
 * Every change is saved by the host and applies to the next Dictation; the rows follow
 * `settings.transcription`. The key's typing state lives here, because what it says (checking, or
 * that the key didn't work) replaces the row's description.
 */
export function useTranscriptionRows(): GroupRows {
  const { bridge, isLanding, toast, confirm } = useHub();
  const view = useTranscriptionSettings(bridge);
  const [edit, setEdit] = useState<KeyEdit | null>(null);
  const previous = useRef(view);

  // A pack finishing is worth saying: the download ran in the background of whatever they were doing.
  useEffect(() => {
    const before = previous.current;
    previous.current = view;
    if (view && before?.downloadingPack && !view.downloadingPack && view.installedPack === before.downloadingPack) {
      toast(`${PACK_NAMES[view.installedPack]} installed. Offline now uses it.`);
    }
  }, [view, toast]);

  // The same for the language model Smart cleanup runs.
  useEffect(() => {
    const before = previous.current;
    if (view && before?.smartCleanupDownloading && !view.smartCleanupDownloading && view.smartCleanupInstalled === before.smartCleanupDownloading) {
      toast(`Smart cleanup is ready (${TIER_NAMES[view.smartCleanupInstalled]} model).`);
    }
  }, [view, toast]);

  const landingOnKey = isLanding("api-key");
  const keyState = view?.keyState;
  useEffect(() => {
    // "Add an API key" (the Overlay) and the status card's fix both land here to fix the key.
    if (landingOnKey && (keyState === "none" || keyState === "rejected")) setEdit({ text: "", phase: "editing" });
  }, [landingOnKey, keyState]);

  const save = useCallback(async () => {
    if (!edit || edit.phase === "checking") return;
    const text = edit.text.trim();
    setEdit({ text, phase: "checking" });
    try {
      const { check } = await bridge.call("settings.saveOnlineKey", { key: text });
      if (check === "didNotWork") {
        setEdit({ text, phase: "didNotWork" });
        return;
      }
      setEdit(null);
      toast(check === "works" ? "Key saved. It works." : "Key saved. There's no internet to check it, so it's checked on your next Dictation.");
    } catch {
      setEdit({ text, phase: "editing" });
      toast("Couldn't save the key. Try again.", { error: true });
    }
  }, [bridge, edit, toast]);

  const removeSmartCleanup = useCallback(async () => {
    const confirmed = await confirm({
      title: "Remove the Smart cleanup model?",
      body: "Smart cleanup turns off and the download is deleted from this PC. Offline keeps working with its plain cleanup. You can download it again later.",
      confirmLabel: "Remove",
    });
    if (confirmed) {
      await bridge.call("settings.removeSmartCleanup", {});
      toast("Smart cleanup removed");
    }
  }, [bridge, confirm, toast]);

  const providerName = view ? PROVIDER_NAMES[view.provider] : "";
  const remove = useCallback(async () => {
    const confirmed = await confirm({
      title: `Remove the ${providerName} key?`,
      body: "Online uses your other saved keys, or stops working until you add a key again. Auto and Offline keep working.",
      confirmLabel: "Remove key",
    });
    if (confirmed) {
      await bridge.call("settings.removeOnlineKey", {});
      toast("Key removed");
    }
  }, [bridge, confirm, toast, providerName]);

  const saveOther = useCallback(
    async (baseUrl: string, model: string) => {
      try {
        const { check } = await bridge.call("settings.setOtherProvider", { baseUrl, model, textModel: "" });
        toast(check === "didNotWork" ? "Saved, but your key didn't work with it." : "Address and model saved.", check === "didNotWork" ? { error: true } : undefined);
      } catch {
        toast("Couldn't save that. The address must start with https://", { error: true });
      }
    },
    [bridge, toast],
  );

  if (!view) return { controls: {}, notes: {}, descriptions: {} };

  const controls: Record<string, ReactNode> = {
    mode: (
      <Segmented
        label="Mode"
        options={modeOptions}
        value={view.mode}
        onChange={(mode) => void bridge.call("settings.setMode", { mode })}
      />
    ),
    "online-provider": (
      <Segmented
        label="Online provider"
        options={PROVIDERS}
        value={view.provider}
        onChange={(provider) => {
          setEdit(null);
          void bridge.call("settings.setProvider", { provider });
        }}
      />
    ),
    "other-provider": <OtherProviderControl other={view.other} onSave={saveOther} />,
    "api-key": (
      <ApiKeyControl
        name={keyName(view.provider)}
        placeholder={KEY_HINTS[view.provider]}
        state={view.keyState}
        mask={view.keyMask}
        edit={edit}
        onStart={() => setEdit({ text: "", phase: "editing" })}
        onChange={(text) => setEdit((e) => (e ? { text, phase: "editing" } : e))}
        onSave={() => void save()}
        onCancel={() => setEdit(null)}
        onRemove={() => void remove()}
      />
    ),
    "default-cleanup": (
      <Segmented
        label="Default cleanup"
        options={cleanupOptions}
        value={cleanupValue(view.defaultCleanup)}
        onChange={(level) => void bridge.call("settings.setDefaultCleanup", { level })}
      />
    ),
    "smart-cleanup": (
      <Switch label="Smart cleanup" checked={view.smartCleanup && (view.smartCleanupInstalled !== null || view.smartCleanupDownloading !== null)} onChange={(on) => void bridge.call("settings.setSmartCleanup", { on })} />
    ),
    "smart-cleanup-model": (
      <SmartCleanupControl
        view={view}
        onPick={(tier: SmartCleanupTier) => void bridge.call("settings.pickSmartCleanup", { tier })}
        onCancel={() => void bridge.call("settings.cancelSmartCleanup", {})}
        onRemove={() => void removeSmartCleanup()}
      />
    ),
    "offline-models": (
      <OfflineModelsControl
        view={view}
        landing={isLanding("offline-models")}
        onPick={(pack: OfflinePack) => void bridge.call("settings.pickOfflinePack", { pack })}
        onCancel={() => void bridge.call("settings.cancelOfflinePack", {})}
      />
    ),
  };

  const notes: Record<string, ReactNode> = {};
  const descriptions: Record<string, ReactNode> = {};
  const labels: Record<string, string> = { "api-key": keyName(view.provider) };
  const hidden = view.provider === "other" ? [] : ["other-provider"];

  // The Mode and the provider rows name the chosen provider (ticket 60).
  descriptions.mode = `Auto uses Online (${providerName}) when you are connected and Offline when you are not.`;
  const backups = view.savedKeys.filter((p) => p !== view.provider).map((p) => PROVIDER_NAMES[p]);
  descriptions["online-provider"] =
    `Online sends your audio and text only to ${providerName}. ` +
    (backups.length > 0
      ? `If it is busy, VoiceCtrl tries your ${backups.join(" and ")} key, then Offline.`
      : "If it is busy, VoiceCtrl tries your other saved keys, then Offline.");

  if (view.provider === "other" && (view.other.baseUrl === "" || view.other.model === "")) {
    notes["other-provider"] = "Add the address and the model, then your key.";
  }

  if (view.mode === "online" && view.keyState === "none" && backups.length === 0) {
    notes.mode = `Online needs ${/^[AEIOU]/.test(providerName) ? "an" : "a"} ${keyName(view.provider)}. Add one below.`;
  }

  if (edit?.phase === "didNotWork") {
    notes["api-key"] = "That key didn't work · Try another";
  } else if (!edit && view.keyState === "rejected") {
    notes["api-key"] = "That key didn't work · Try another";
  }

  if (view.smartCleanupDownloading !== null && view.smartCleanupDownload !== null) {
    if (view.smartCleanupDownload.state === "failed") {
      notes["smart-cleanup-model"] = "The download stopped. Check your internet connection, then try again.";
    } else {
      descriptions["smart-cleanup-model"] = tierDownloadLine(view.smartCleanupDownloading, view.smartCleanupDownload);
    }
  } else if (view.smartCleanupInstalled !== null) {
    descriptions["smart-cleanup-model"] = `${TIER_NAMES[view.smartCleanupInstalled]} model installed: ${TIER_HINTS[view.smartCleanupInstalled]}.`;
  } else if (view.smartCleanup) {
    notes["smart-cleanup-model"] = "Not downloaded yet, so Smart cleanup is off. Turn it on to download it; Offline uses its plain cleanup until then.";
  }

  if (view.downloadingPack !== null && view.download !== null) {
    if (view.download.state === "failed") {
      notes["offline-models"] = "The download stopped. Check your internet connection, then try again.";
    } else {
      descriptions["offline-models"] = packDownloadLine(view.downloadingPack, view.download);
    }
  } else if (view.installedPack !== "none") {
    const info = view.packs.find((p) => p.pack === view.installedPack);
    descriptions["offline-models"] = `${PACK_NAMES[view.installedPack]} installed${info ? ` · ${packLanguages(info)}` : ""}. Picking another pack replaces it.`;
  }

  return { controls, notes, descriptions, labels, hidden };
}

/** A level nobody wrote (a hand-edited file) shows as Standard, which is what the host uses for it. */
function cleanupValue(level: string): (typeof cleanupOptions)[number]["value"] {
  return cleanupOptions.find((o) => o.value === level)?.value ?? "standard";
}
