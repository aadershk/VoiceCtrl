import { useId } from "react";
import type { InputHTMLAttributes, ReactNode, Ref, TextareaHTMLAttributes } from "react";
import { Icon } from "./Icon";

export interface SearchFieldProps extends Omit<InputHTMLAttributes<HTMLInputElement>, "type"> {
  /** The accessible name; shown as the placeholder unless one is given. */
  label: string;
  ref?: Ref<HTMLInputElement>;
}

/** Search and Quick Find field (§8): 38 h on `--tint`, no border, leading search icon. */
export function SearchField({ label, placeholder, className, ...rest }: SearchFieldProps) {
  return (
    <label className={className ? `vc-search ${className}` : "vc-search"}>
      <Icon name="search" />
      <input type="search" aria-label={label} placeholder={placeholder ?? label} {...rest} />
    </label>
  );
}

interface FieldShellProps {
  label: string;
  /** Shown under the control with an alert icon; also marks the control invalid (§8). */
  error?: string | undefined;
}

/**
 * Label, control and error. The error sits outside the <label>, so it is read as the control's
 * description rather than folded into its name.
 */
function FieldShell({ id, label, error, errorId, children }: FieldShellProps & { id: string; errorId: string; children: ReactNode }) {
  return (
    <div className="vc-field">
      <label htmlFor={id}>{label}</label>
      {children}
      {error && (
        <span id={errorId} className="vc-field-error" role="alert">
          <Icon name="alert" size={16} />
          {error}
        </span>
      )}
    </div>
  );
}

export interface TextFieldProps extends FieldShellProps, InputHTMLAttributes<HTMLInputElement> {
  ref?: Ref<HTMLInputElement>;
}

/** Text input (§8): 40 h, 1 px `--line-2` on `--panel`, 15 px; focus border `--ink`. */
export function TextField({ label, error, id, ...rest }: TextFieldProps) {
  const autoId = useId();
  const inputId = id ?? autoId;
  const errorId = `${inputId}-error`;
  return (
    <FieldShell id={inputId} errorId={errorId} label={label} error={error}>
      <input
        id={inputId}
        className="vc-input"
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? errorId : undefined}
        {...rest}
      />
    </FieldShell>
  );
}

export interface TextAreaProps extends FieldShellProps, TextareaHTMLAttributes<HTMLTextAreaElement> {
  ref?: Ref<HTMLTextAreaElement>;
}

/** Textarea (§8): 76 h, the text input's styling, no resize. */
export function TextArea({ label, error, id, ...rest }: TextAreaProps) {
  const autoId = useId();
  const inputId = id ?? autoId;
  const errorId = `${inputId}-error`;
  return (
    <FieldShell id={inputId} errorId={errorId} label={label} error={error}>
      <textarea
        id={inputId}
        className="vc-input"
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? errorId : undefined}
        {...rest}
      />
    </FieldShell>
  );
}
