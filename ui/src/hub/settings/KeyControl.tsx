import { useEffect, useRef } from "react";
import { Button, Keys } from "../../design/components";

export interface KeyControlProps {
  /** The key chips; null when the key isn't set (only the Trigger Key can be unset). */
  keys: readonly string[] | null;
  /** This row is listening. */
  capturing: boolean;
  /** What the listening button says, e.g. "Press a key". */
  prompt: string;
  /** The row's name, so each "Change" button is labelled for screen readers. */
  name: string;
  onChange: () => void;
  onCancel: () => void;
  /** Offered only where the key can be turned off (Trigger Key). */
  onRemove?: () => void;
}

/**
 * A key row's control (research 10 Settings): the key chips and Change, or, while listening,
 * "Press a key" and Cancel. Esc also cancels; the host hears it even though the page doesn't.
 */
export function KeyControl({ keys, capturing, prompt, name, onChange, onCancel, onRemove }: KeyControlProps) {
  const changeRef = useRef<HTMLButtonElement>(null);
  const cancelRef = useRef<HTMLButtonElement>(null);
  const wasCapturing = useRef(false);

  // Focus follows the swap, so a keyboard user is never left on a button that vanished.
  useEffect(() => {
    if (capturing) cancelRef.current?.focus({ preventScroll: true });
    else if (wasCapturing.current) changeRef.current?.focus({ preventScroll: true });
    wasCapturing.current = capturing;
  }, [capturing]);

  if (capturing) {
    return (
      <>
        <span className="hub-key-listening" role="status">
          {prompt}
        </span>
        <Button ref={cancelRef} variant="ghost" size="sm" onClick={onCancel}>
          Cancel
        </Button>
      </>
    );
  }

  return (
    <>
      {keys ? <Keys keys={keys} /> : <span className="hub-setting-muted">Not set</span>}
      <Button ref={changeRef} size="sm" aria-label={keys ? `Change ${name}` : `Set up ${name}`} onClick={onChange}>
        {keys ? "Change" : "Set up"}
      </Button>
      {keys && onRemove && (
        <Button variant="ghost" size="sm" aria-label={`Remove ${name}`} onClick={onRemove}>
          Remove
        </Button>
      )}
    </>
  );
}
