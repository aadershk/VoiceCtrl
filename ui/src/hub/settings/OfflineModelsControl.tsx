import { useEffect, useRef, useState } from "react";
import type { OfflinePack, TranscriptionSettingsView } from "../../bridge/contracts.g";
import { Button, Menu, MenuAnchor, MenuItem, MenuSeparator, ProgressBar } from "../../design/components";
import { downloadPercent } from "../../setup/format";
import { PACK_NAMES, packFraction, packMenuLabel } from "./packs";

export interface OfflineModelsControlProps {
  view: TranscriptionSettingsView;
  /** Opens the menu on arrival, so a Quick Find landing lands on something usable. */
  landing: boolean;
  onPick: (pack: OfflinePack) => void;
  onCancel: () => void;
}

/**
 * The Offline models row (research 10 Settings): a dropdown to choose the one extra language pack,
 * then a bar with the percentage and Cancel while it downloads, or Try again if it stopped. The
 * English model from Setup is always there; picking another pack replaces the installed one.
 */
export function OfflineModelsControl({ view, landing, onPick, onCancel }: OfflineModelsControlProps) {
  const [open, setOpen] = useState(false);
  const pickRef = useRef<HTMLButtonElement>(null);
  const cancelRef = useRef<HTMLButtonElement>(null);
  const busy = view.downloadingPack !== null && view.download !== null;
  const wasBusy = useRef(false);

  useEffect(() => {
    if (landing && !busy) setOpen(true);
  }, [landing, busy]);

  // Focus follows the swap, so a keyboard user is never left on a button that vanished.
  useEffect(() => {
    if (busy && !wasBusy.current) cancelRef.current?.focus({ preventScroll: true });
    else if (!busy && wasBusy.current) pickRef.current?.focus({ preventScroll: true });
    wasBusy.current = busy;
  }, [busy]);

  if (view.downloadingPack !== null && view.download !== null) {
    const name = PACK_NAMES[view.downloadingPack];
    const failed = view.download.state === "failed";
    return (
      <>
        {failed ? (
          <Button size="sm" aria-label={`Try ${name} again`} onClick={() => onPick(view.downloadingPack as OfflinePack)}>
            Try again
          </Button>
        ) : (
          <>
            <ProgressBar value={packFraction(view.download)} label={`Downloading ${name}`} width={140} />
            <span className="hub-pack-percent" aria-hidden="true">
              {downloadPercent(view.download)}%
            </span>
          </>
        )}
        <Button ref={cancelRef} variant="ghost" size="sm" aria-label={`Cancel ${name}`} onClick={onCancel}>
          Cancel
        </Button>
      </>
    );
  }

  const label = PACK_NAMES[view.installedPack];
  return (
    <MenuAnchor>
      <Button
        ref={pickRef}
        className="hub-pack-button"
        trailingIcon="chevronDown"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label={`Offline language pack: ${label}`}
        onClick={() => setOpen((o) => !o)}
      >
        <span className="hub-pack-label">{label}</span>
      </Button>
      <Menu open={open} onClose={() => setOpen(false)} label="Offline language pack" placement="below-end">
        <MenuItem selected={view.installedPack === "none"} onSelect={() => onPick("none")}>
          {PACK_NAMES.none}
        </MenuItem>
        <MenuSeparator />
        {view.packs.map((info) => (
          <MenuItem key={info.pack} selected={view.installedPack === info.pack} onSelect={() => onPick(info.pack)}>
            {packMenuLabel(info)}
          </MenuItem>
        ))}
      </Menu>
    </MenuAnchor>
  );
}
