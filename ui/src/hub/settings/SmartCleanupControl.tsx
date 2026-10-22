import { useEffect, useRef } from "react";
import type { SmartCleanupTier, TranscriptionSettingsView } from "../../bridge/contracts.g";
import { Button, ProgressBar, Segmented } from "../../design/components";
import { downloadPercent } from "../../setup/format";
import { TIER_NAMES, tierFraction, tierLabel } from "./smartCleanup";

export interface SmartCleanupControlProps {
  view: TranscriptionSettingsView;
  onPick: (tier: SmartCleanupTier) => void;
  onCancel: () => void;
  onRemove: () => void;
}

/**
 * The Smart cleanup model row (ticket 37): the two models as a choice, then Download when none is on
 * disk or Remove when one is. While a model downloads the choice gives way to a bar with the
 * percentage and Cancel, or Try again if it stopped, like the Offline models row.
 */
export function SmartCleanupControl({ view, onPick, onCancel, onRemove }: SmartCleanupControlProps) {
  const cancelRef = useRef<HTMLButtonElement>(null);
  const actionRef = useRef<HTMLButtonElement>(null);
  const busy = view.smartCleanupDownloading !== null && view.smartCleanupDownload !== null;
  const wasBusy = useRef(false);

  // Focus follows the swap, so a keyboard user is never left on a control that vanished.
  useEffect(() => {
    if (busy && !wasBusy.current) cancelRef.current?.focus({ preventScroll: true });
    else if (!busy && wasBusy.current) actionRef.current?.focus({ preventScroll: true });
    wasBusy.current = busy;
  }, [busy]);

  if (view.smartCleanupDownloading !== null && view.smartCleanupDownload !== null) {
    const tier = view.smartCleanupDownloading;
    const name = TIER_NAMES[tier];
    const failed = view.smartCleanupDownload.state === "failed";
    return (
      <>
        {failed ? (
          <Button size="sm" aria-label={`Try ${name} again`} onClick={() => onPick(tier)}>
            Try again
          </Button>
        ) : (
          <>
            <ProgressBar value={tierFraction(view.smartCleanupDownload)} label={`Downloading the ${name} model`} width={140} />
            <span className="hub-pack-percent" aria-hidden="true">
              {downloadPercent(view.smartCleanupDownload)}%
            </span>
          </>
        )}
        <Button ref={cancelRef} variant="ghost" size="sm" aria-label={`Cancel ${name}`} onClick={onCancel}>
          Cancel
        </Button>
      </>
    );
  }

  const installed = view.smartCleanupInstalled !== null;
  return (
    <>
      <Segmented
        label="Smart cleanup model"
        options={view.smartCleanupPacks.map((info) => ({ value: info.tier, label: tierLabel(info) }))}
        value={view.smartCleanupInstalled ?? view.smartCleanupTier}
        onChange={onPick}
      />
      {installed ? (
        <Button ref={actionRef} variant="ghost" size="sm" onClick={onRemove}>
          Remove
        </Button>
      ) : (
        <Button ref={actionRef} size="sm" onClick={() => onPick(view.smartCleanupTier)}>
          Download
        </Button>
      )}
    </>
  );
}
