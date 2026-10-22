import { createContext, useContext, useEffect, useId, useRef } from "react";
import type { KeyboardEvent, ReactNode } from "react";
import { cx } from "../cx";
import { fadeIn, fadeOut, popoverEnter, popoverExit, usePresence } from "../motion";
import type { IconName } from "../icons";
import { Button, IconButton } from "./Button";
import { Icon } from "./Icon";

const MenuClose = createContext<() => void>(() => undefined);

export type MenuPlacement = "below-start" | "below-end" | "above-start" | "above-end" | "static";

const ORIGIN = {
  "below-start": "top left",
  "below-end": "top right",
  "above-start": "bottom left",
  "above-end": "bottom right",
  static: "bottom right",
} as const;

/** Wraps a menu's opener and the menu, so the menu is placed from the opener. */
export function MenuAnchor({ children }: { children: ReactNode }) {
  return <span className="vc-menu-anchor">{children}</span>;
}

export interface MenuProps {
  open: boolean;
  onClose: () => void;
  label: string;
  /** Grows from the opener's nearest corner. `static` is the tray flyout, which is the page. */
  placement?: MenuPlacement;
  children: ReactNode;
}

// Radio items too: the tray flyout's Mode control is made of them.
function menuItems(menu: HTMLElement): HTMLButtonElement[] {
  return Array.from(
    menu.querySelectorAll<HTMLButtonElement>('[role="menuitem"]:not(:disabled), [role="menuitemradio"]:not(:disabled)'),
  );
}

/**
 * Menu (§8): closes on a pick, Esc or a click outside; arrow keys move, Home/End jump, Enter
 * picks. Focus moves to the first item on open and back to the opener on close.
 */
export function Menu({ open, onClose, label, placement = "below-start", children }: MenuProps) {
  const { mounted, ref } = usePresence<HTMLDivElement>(open, {
    enter: (el) => popoverEnter(el, ORIGIN[placement]),
    exit: popoverExit,
  });
  const opener = useRef<Element | null>(null);

  // Runs once the menu is actually mounted: usePresence mounts it a render after `open` flips.
  useEffect(() => {
    const menu = ref.current;
    if (!open || !mounted || !menu) return;
    if (!menu.contains(document.activeElement)) {
      opener.current = document.activeElement;
      menuItems(menu)[0]?.focus({ preventScroll: true });
    }
    const onPointerDown = (event: PointerEvent) => {
      const scope = placement === "static" ? menu : menu?.parentElement;
      if (scope && event.target instanceof Node && !scope.contains(event.target)) onClose();
    };
    document.addEventListener("pointerdown", onPointerDown);
    return () => document.removeEventListener("pointerdown", onPointerDown);
  }, [open, mounted, onClose, placement, ref]);

  const close = () => {
    onClose();
    if (opener.current instanceof HTMLElement) opener.current.focus();
  };

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const menu = ref.current;
    if (!menu) return;
    const items = menuItems(menu);
    const at = items.indexOf(document.activeElement as HTMLButtonElement);
    const target = {
      ArrowDown: items[(at + 1) % items.length],
      // From the menu itself (nothing focused yet), Up goes to the last item.
      ArrowUp: items[at <= 0 ? items.length - 1 : at - 1],
      Home: items[0],
      End: items[items.length - 1],
    }[event.key];
    if (target) {
      event.preventDefault();
      target.focus();
    } else if (event.key === "Escape" || event.key === "Tab") {
      event.preventDefault();
      close();
    }
  };

  if (!mounted) return null;
  return (
    <MenuClose.Provider value={close}>
      <div
        ref={ref}
        role="menu"
        aria-label={label}
        className={cx("vc-menu", placement !== "static" && `vc-menu-pop vc-menu-${placement}`)}
        onKeyDown={onKeyDown}
      >
        {children}
      </div>
    </MenuClose.Provider>
  );
}

export interface MenuItemProps {
  icon?: IconName;
  danger?: boolean;
  disabled?: boolean;
  /** A pick-one menu (the microphone): every item gets a check column, ticked on the chosen one. */
  selected?: boolean;
  onSelect: () => void;
  children: ReactNode;
}

export function MenuItem({ icon, danger = false, disabled = false, selected, onSelect, children }: MenuItemProps) {
  const close = useContext(MenuClose);
  return (
    <button
      type="button"
      role="menuitem"
      tabIndex={-1}
      disabled={disabled}
      aria-current={selected ? "true" : undefined}
      className={cx("vc-menu-item", danger && "vc-menu-item-danger")}
      onClick={() => {
        close();
        onSelect();
      }}
    >
      {selected !== undefined && (
        <span className="vc-menu-check">{selected && <Icon name="check" size={16} />}</span>
      )}
      {icon && <Icon name={icon} size={16} />}
      <span className="vc-menu-label">{children}</span>
    </button>
  );
}

export function MenuSeparator() {
  return <div className="vc-menu-sep" role="separator" />;
}

export interface DialogProps {
  open: boolean;
  title: string;
  children: ReactNode;
  confirmLabel: string;
  onConfirm: () => void;
  onCancel: () => void;
}

/**
 * Dialog (§8): centred on `--scrim`, Sora 20 title, Cancel focused by default. Only Clear all
 * Dictations uses one (§11: undo instead of confirm everywhere else), so it is always destructive.
 */
export function Dialog({ open, title, children, confirmLabel, onConfirm, onCancel }: DialogProps) {
  const titleId = useId();
  const { mounted, ref } = usePresence<HTMLDivElement>(open, {
    enter: (scrim) => {
      fadeIn(scrim);
      if (scrim.firstElementChild) popoverEnter(scrim.firstElementChild, "top center");
    },
    exit: async (scrim) => {
      await Promise.all([
        fadeOut(scrim),
        scrim.firstElementChild ? popoverExit(scrim.firstElementChild) : Promise.resolve(),
      ]);
    },
  });
  const cancelRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (open) cancelRef.current?.focus({ preventScroll: true });
  }, [open, mounted]);

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === "Escape") {
      event.preventDefault();
      onCancel();
      return;
    }
    if (event.key !== "Tab") return;
    // Keep focus inside the dialog while it is open.
    const buttons = Array.from(event.currentTarget.querySelectorAll<HTMLElement>("button:not(:disabled)"));
    const first = buttons[0];
    const last = buttons[buttons.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last?.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first?.focus();
    }
  };

  if (!mounted) return null;
  return (
    <div
      ref={ref}
      className="vc-scrim"
      onPointerDown={(event) => {
        if (event.target === event.currentTarget) onCancel();
      }}
      onKeyDown={onKeyDown}
    >
      <div className="vc-dialog" role="dialog" aria-modal="true" aria-labelledby={titleId}>
        <h2 id={titleId} className="vc-dialog-title">
          {title}
        </h2>
        <p className="vc-dialog-body">{children}</p>
        <div className="vc-dialog-actions">
          <Button ref={cancelRef} variant="ghost" onClick={onCancel}>
            Cancel
          </Button>
          <Button variant="danger" onClick={onConfirm}>
            {confirmLabel}
          </Button>
        </div>
      </div>
    </div>
  );
}

export interface ToastProps {
  open: boolean;
  message: string;
  /** Error toasts are `--panel` with a danger icon. */
  error?: boolean;
  /** Undo or another single action; the toast then stays 5 s instead of 3.2 s. */
  action?: { label: string; onClick: () => void } | undefined;
  onClose: () => void;
}

export const TOAST_MS = 3200;
export const TOAST_WITH_ACTION_MS = 5000;

/** Toast (§8): bottom centre of the content card; place it inside a positioned container. */
export function Toast({ open, message, error = false, action, onClose }: ToastProps) {
  const { mounted, ref } = usePresence<HTMLDivElement>(open, {
    enter: (el) => popoverEnter(el, "bottom center"),
    exit: popoverExit,
  });

  // The timer restarts only for a new message, not when the parent passes a fresh callback.
  const latestClose = useRef(onClose);
  latestClose.current = onClose;
  const hasAction = action !== undefined;
  useEffect(() => {
    if (!open) return;
    const timer = setTimeout(() => latestClose.current(), hasAction ? TOAST_WITH_ACTION_MS : TOAST_MS);
    return () => clearTimeout(timer);
  }, [open, message, hasAction]);

  return (
    <div className="vc-toast-region" role="status" aria-live="polite">
      {mounted && (
        <div ref={ref} className={cx("vc-toast", error && "vc-toast-error")}>
          {error && <Icon name="alert" size={16} />}
          <span className="vc-toast-message">{message}</span>
          {action && (
            <Button variant="ghost" size="sm" className="vc-toast-action" onClick={action.onClick}>
              {action.label}
            </Button>
          )}
          <IconButton icon="close" label="Close" size="sm" onClick={onClose} />
        </div>
      )}
    </div>
  );
}
