import type { ButtonHTMLAttributes, Ref } from "react";
import { cx } from "../cx";
import type { IconName } from "../icons";
import { Icon } from "./Icon";

export type ButtonVariant = "primary" | "secondary" | "ghost" | "danger";

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  /** One primary per view; danger only in the Clear all dialog (§8). */
  variant?: ButtonVariant;
  size?: "md" | "sm";
  icon?: IconName;
  trailingIcon?: IconName;
  block?: boolean;
  ref?: Ref<HTMLButtonElement>;
}

export function Button({
  variant = "secondary",
  size = "md",
  icon,
  trailingIcon,
  block = false,
  className,
  children,
  type = "button",
  ...rest
}: ButtonProps) {
  return (
    <button
      type={type}
      className={cx("vc-btn", `vc-btn-${variant}`, size === "sm" && "vc-btn-sm", block && "vc-btn-block", className)}
      {...rest}
    >
      {icon && <Icon name={icon} size={16} />}
      {children}
      {trailingIcon && <Icon name={trailingIcon} size={16} />}
    </button>
  );
}

export interface IconButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, "children"> {
  icon: IconName;
  /** Required: an icon-only button needs a name for screen readers and a tooltip. */
  label: string;
  size?: "md" | "sm";
  ref?: Ref<HTMLButtonElement>;
}

export function IconButton({ icon, label, size = "md", className, type = "button", ...rest }: IconButtonProps) {
  return (
    <button
      type={type}
      aria-label={label}
      title={label}
      className={cx("vc-icon-btn", size === "sm" && "vc-icon-btn-sm", className)}
      {...rest}
    >
      <Icon name={icon} size={size === "sm" ? 16 : 18} />
    </button>
  );
}

export interface WindowControlsProps {
  onMinimize: () => void;
  onClose: () => void;
  /** Omit for fixed-size windows (Setup). */
  onToggleMaximize?: () => void;
  /** The window is maximized: the middle button restores instead (the host's `window.state`). */
  maximized?: boolean;
}

/** The frameless window's own caption buttons (§5); they call the bridge through the handlers. */
export function WindowControls({ onMinimize, onToggleMaximize, onClose, maximized = false }: WindowControlsProps) {
  return (
    <div className="vc-window-controls">
      <button type="button" className="vc-window-button" aria-label="Minimize" onClick={onMinimize}>
        <Icon name="minimize" size={14} />
      </button>
      {onToggleMaximize && (
        <button type="button" className="vc-window-button" aria-label={maximized ? "Restore" : "Maximize"} onClick={onToggleMaximize}>
          <Icon name={maximized ? "restore" : "maximize"} size={14} />
        </button>
      )}
      <button type="button" className="vc-window-button vc-window-close" aria-label="Close" onClick={onClose}>
        <Icon name="close" size={14} />
      </button>
    </div>
  );
}
