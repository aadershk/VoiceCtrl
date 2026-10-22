import { useCallback, useEffect, useState } from "react";
import type { ReactNode } from "react";
import type { SuggestionSettingsView } from "../../bridge/contracts.g";
import { Switch } from "../../design/components";
import { useHub } from "../hubContext";
import { ApiKeyControl } from "./ApiKeyControl";
import type { KeyEdit } from "./ApiKeyControl";
import type { GroupRows } from "./dictationRows";

/**
 * The Word suggestions group (slice 24): the "Suggest Dictionary words" switch and the optional
 * TypeSafe key. The key row works like the Gemini one: checked with TypeSafe before it is saved,
 * masked once saved. The rows follow `settings.suggestions`.
 */
export function useSuggestionRows(): GroupRows {
  const { bridge, toast, confirm } = useHub();
  const [view, setView] = useState<SuggestionSettingsView | null>(null);
  const [edit, setEdit] = useState<KeyEdit | null>(null);

  useEffect(() => {
    let live = true;
    const off = bridge.on("settings.suggestions", ({ suggestions }) => setView(suggestions));
    bridge.call("settings.getSuggestions", {}).then((v) => live && setView(v), () => undefined);
    return () => {
      live = false;
      off();
    };
  }, [bridge]);

  const save = useCallback(async () => {
    if (!edit || edit.phase === "checking") return;
    const text = edit.text.trim();
    setEdit({ text, phase: "checking" });
    try {
      const { check } = await bridge.call("settings.saveTypeSafeKey", { key: text });
      if (check === "didNotWork") {
        setEdit({ text, phase: "didNotWork" });
        return;
      }
      setEdit(null);
      toast(check === "works" ? "Key saved. It works." : "Key saved. It's checked the next time suggestions are made.");
    } catch {
      setEdit({ text, phase: "editing" });
      toast("Couldn't save the key. Try again.", { error: true });
    }
  }, [bridge, edit, toast]);

  const remove = useCallback(async () => {
    const confirmed = await confirm({
      title: "Remove the TypeSafe key?",
      body: "Suggestions keep coming, found by VoiceCtrl alone, and nothing is sent to TypeSafe.",
      confirmLabel: "Remove key",
    });
    if (confirmed) {
      await bridge.call("settings.removeTypeSafeKey", {});
      toast("Key removed");
    }
  }, [bridge, confirm, toast]);

  if (!view) return { controls: {}, notes: {} };

  const controls: Record<string, ReactNode> = {
    "suggest-words": (
      <Switch
        label="Suggest Dictionary words"
        checked={view.on}
        onChange={(on) => void bridge.call("settings.setSuggestWords", { on })}
      />
    ),
    "typesafe-key": (
      <ApiKeyControl
        name="TypeSafe key"
        placeholder="Paste your TypeSafe key"
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
  };

  const notes: Record<string, ReactNode> = {};
  if (edit?.phase === "didNotWork" || (!edit && view.keyState === "rejected")) {
    notes["typesafe-key"] = "That key didn't work · Try another";
  }

  return { controls, notes };
}
