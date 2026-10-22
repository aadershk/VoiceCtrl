import { useRef } from "react";
import type { KeyboardEvent, ReactNode } from "react";
import type { IconName } from "../icons";
import { Icon } from "./Icon";

export interface SwitchProps {
  checked: boolean;
  onChange: (checked: boolean) => void;
  /** The accessible name, normally the Settings row label. */
  label: string;
  disabled?: boolean;
}

/** Switch (§8): 40 × 24 track, 18 px knob that slides 16 px on the spring. */
export function Switch({ checked, onChange, label, disabled = false }: SwitchProps) {
  return (
    <button
      type="button"
      role="switch"
      className="vc-switch"
      aria-checked={checked}
      aria-label={label}
      disabled={disabled}
      onClick={() => onChange(!checked)}
    />
  );
}

export interface Option<T extends string> {
  value: T;
  label: string;
}

/**
 * Arrow keys move and select within a radio group, as Windows does; Tab leaves the group.
 * Returns the handler and whether a given index takes the group's single tab stop.
 */
function useRovingRadio<T extends string>(values: readonly T[], value: T, onChange: (value: T) => void) {
  const refs = useRef<Array<HTMLButtonElement | null>>([]);
  const onKeyDown = (event: KeyboardEvent) => {
    const step = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[event.key];
    if (step === undefined) return;
    event.preventDefault();
    const next = (values.indexOf(value) + step + values.length) % values.length;
    const nextValue = values[next];
    if (nextValue === undefined) return;
    onChange(nextValue);
    refs.current[next]?.focus();
  };
  const hasSelection = values.includes(value);
  const tabIndexFor = (index: number) => ((hasSelection ? values[index] === value : index === 0) ? 0 : -1);
  return { refs, onKeyDown, tabIndexFor };
}

export interface SegmentedProps<T extends string> {
  options: readonly Option<T>[];
  value: T;
  onChange: (value: T) => void;
  label: string;
}

/** Segmented control (§8): `--tint` track, the selected 30 h segment raised. */
export function Segmented<T extends string>({ options, value, onChange, label }: SegmentedProps<T>) {
  const roving = useRovingRadio(options.map((o) => o.value), value, onChange);
  return (
    <div className="vc-seg" role="radiogroup" aria-label={label} onKeyDown={roving.onKeyDown}>
      {options.map((option, index) => (
        <button
          key={option.value}
          ref={(el) => {
            roving.refs.current[index] = el;
          }}
          type="button"
          role="radio"
          aria-checked={option.value === value}
          tabIndex={roving.tabIndexFor(index)}
          onClick={() => onChange(option.value)}
        >
          {option.label}
        </button>
      ))}
    </div>
  );
}

export interface RadioCardOption<T extends string> extends Option<T> {
  description: ReactNode;
  icon: IconName;
  badge?: string;
}

export interface RadioCardsProps<T extends string> {
  options: readonly RadioCardOption<T>[];
  value: T;
  onChange: (value: T) => void;
  label: string;
}

/** Radio cards (Setup, §8): selected gets a 2 px `--ink` ring and a dot that grows on the spring. */
export function RadioCards<T extends string>({ options, value, onChange, label }: RadioCardsProps<T>) {
  const roving = useRovingRadio(options.map((o) => o.value), value, onChange);
  return (
    <div className="vc-radio-group" role="radiogroup" aria-label={label} onKeyDown={roving.onKeyDown}>
      {options.map((option, index) => (
        <button
          key={option.value}
          ref={(el) => {
            roving.refs.current[index] = el;
          }}
          type="button"
          role="radio"
          className="vc-radio-card"
          aria-checked={option.value === value}
          tabIndex={roving.tabIndexFor(index)}
          onClick={() => onChange(option.value)}
        >
          <span className="vc-radio-card-tile">
            <Icon name={option.icon} />
          </span>
          <span className="vc-radio-card-text">
            <span className="vc-radio-card-title">
              {option.label}
              {option.badge && <span className="vc-badge">{option.badge}</span>}
            </span>
            <span className="vc-radio-card-desc">{option.description}</span>
          </span>
          <span className="vc-radio-dot" aria-hidden="true" />
        </button>
      ))}
    </div>
  );
}
