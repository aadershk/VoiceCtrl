import { useEffect, useState } from "react";
import type { OtherProviderSettings } from "../../bridge/contracts.g";
import { Button } from "../../design/components";

export interface OtherProviderControlProps {
  other: OtherProviderSettings;
  /** Saves the address and model; resolves once the host answered. */
  onSave: (baseUrl: string, model: string) => Promise<void>;
}

/**
 * The Other provider's address and model (ticket 60): two fields and Save, the key row's pattern.
 * The fields start as saved and Save lights up once either changes.
 */
export function OtherProviderControl({ other, onSave }: OtherProviderControlProps) {
  const [baseUrl, setBaseUrl] = useState(other.baseUrl);
  const [model, setModel] = useState(other.model);
  const [saving, setSaving] = useState(false);

  // A save elsewhere (or the host's own update) shows here.
  useEffect(() => {
    setBaseUrl(other.baseUrl);
    setModel(other.model);
  }, [other.baseUrl, other.model]);

  const changed = baseUrl.trim() !== other.baseUrl || model.trim() !== other.model;
  const ready = baseUrl.trim().length > 0 && model.trim().length > 0;
  const save = async () => {
    if (!changed || !ready || saving) return;
    setSaving(true);
    try {
      await onSave(baseUrl.trim(), model.trim());
    } finally {
      setSaving(false);
    }
  };

  return (
    <>
      <input
        className="vc-input hub-other-address"
        aria-label="Address"
        placeholder="https://api.example.com/v1"
        autoComplete="off"
        spellCheck={false}
        value={baseUrl}
        disabled={saving}
        onChange={(event) => setBaseUrl(event.target.value)}
        onKeyDown={(event) => event.key === "Enter" && void save()}
      />
      <input
        className="vc-input hub-other-model"
        aria-label="Model"
        placeholder="Model"
        autoComplete="off"
        spellCheck={false}
        value={model}
        disabled={saving}
        onChange={(event) => setModel(event.target.value)}
        onKeyDown={(event) => event.key === "Enter" && void save()}
      />
      <Button variant="primary" size="sm" disabled={!changed || !ready || saving} onClick={() => void save()}>
        Save
      </Button>
    </>
  );
}
