import { useEffect, useRef } from "react";
import type { KeyboardEvent } from "react";
import type { ApiKeyState } from "../../bridge/contracts.g";
import { Button, Icon, Spinner } from "../../design/components";

/** The key being typed: a fresh field (`editing`), being checked, or refused by the service. */
export interface KeyEdit {
  text: string;
  phase: "editing" | "checking" | "didNotWork";
}

export interface ApiKeyControlProps {
  /** The row's name, e.g. "Gemini API key", for screen readers. */
  name: string;
  /** The empty field's hint, e.g. "Starts with AIza". */
  placeholder: string;
  state: ApiKeyState;
  /** "AIza ···· 3f9a": all the page ever knows of a saved key. */
  mask: string | null;
  edit: KeyEdit | null;
  onStart: () => void;
  onChange: (text: string) => void;
  onSave: () => void;
  onCancel: () => void;
  onRemove: () => void;
}

/**
 * An API key row (research 10 Settings): the Gemini key, and the TypeSafe key of Word suggestions.
 * Saved: the masked key with what is known of it (Works, Saved, or that the service refused it) and
 * Change or Remove. Editing: a password field, Save and Cancel; Save checks the key with the service
 * first, so a typo never replaces a working key. The sentence about a refused key is the row's
 * description (see `useTranscriptionRows`).
 */
export function ApiKeyControl({ name, placeholder, state, mask, edit, onStart, onChange, onSave, onCancel, onRemove }: ApiKeyControlProps) {
  const inputRef = useRef<HTMLInputElement>(null);
  const startRef = useRef<HTMLButtonElement>(null);
  const wasEditing = useRef(false);
  const editing = edit !== null;

  // Focus follows the swap, so a keyboard user is never left on a button that vanished.
  useEffect(() => {
    if (editing && !wasEditing.current) inputRef.current?.focus({ preventScroll: true });
    else if (!editing && wasEditing.current) startRef.current?.focus({ preventScroll: true });
    wasEditing.current = editing;
  }, [editing]);

  if (edit) {
    const checking = edit.phase === "checking";
    const submitOnEnter = (event: KeyboardEvent<HTMLInputElement>) => {
      if (event.key === "Enter" && edit.text.trim() && !checking) onSave();
      else if (event.key === "Escape") onCancel();
    };
    return (
      <>
        <input
          ref={inputRef}
          className="vc-input hub-key-input"
          type="password"
          aria-label={name}
          aria-invalid={edit.phase === "didNotWork" ? true : undefined}
          placeholder={placeholder}
          autoComplete="off"
          spellCheck={false}
          value={edit.text}
          disabled={checking}
          onChange={(event) => onChange(event.target.value)}
          onKeyDown={submitOnEnter}
        />
        {checking && (
          <span className="hub-key-checking" role="status">
            <Spinner />
            Checking the key
          </span>
        )}
        <Button variant="primary" size="sm" disabled={checking || edit.text.trim().length === 0} onClick={onSave}>
          Save
        </Button>
        <Button variant="ghost" size="sm" disabled={checking} onClick={onCancel}>
          Cancel
        </Button>
      </>
    );
  }

  if (state === "none") {
    return (
      <>
        <span className="hub-setting-muted">Not set</span>
        <Button ref={startRef} size="sm" aria-label={`Add ${name}`} onClick={onStart}>
          Add key
        </Button>
      </>
    );
  }

  return (
    <>
      <span className="hub-key-mask" aria-label={`Saved key ${mask ?? ""}`}>
        {mask}
      </span>
      {state === "works" && (
        <span className="hub-key-ok">
          <Icon name="check" size={16} />
          Works
        </span>
      )}
      {state === "saved" && <span className="hub-setting-muted">Saved</span>}
      <Button ref={startRef} size="sm" aria-label={state === "rejected" ? `Try another ${name}` : `Change ${name}`} onClick={onStart}>
        {state === "rejected" ? "Try another" : "Change"}
      </Button>
      <Button variant="ghost" size="sm" aria-label={`Remove ${name}`} onClick={onRemove}>
        Remove
      </Button>
    </>
  );
}
