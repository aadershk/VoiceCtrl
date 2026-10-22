import { cx } from "../cx";
import { icons } from "../icons";
import type { IconName, IconSize } from "../icons";

export interface IconProps {
  name: IconName;
  size?: IconSize;
  /** Gives the icon a name for screen readers; without it the icon is decorative. */
  label?: string;
  className?: string;
}

export function Icon({ name, size = 18, label, className }: IconProps) {
  return (
    <svg
      className={cx("vc-icon", size !== 18 && `vc-icon-${size}`, className)}
      viewBox="0 0 24 24"
      focusable="false"
      role={label ? "img" : undefined}
      aria-label={label}
      aria-hidden={label ? undefined : true}
      // Static markup from icons.ts, never user input.
      dangerouslySetInnerHTML={{ __html: icons[name] }}
    />
  );
}

/**
 * The mark (§6): a squircle tile in `--tile` with three coral bars, short–tall–medium.
 * Drawn from tokens, never from the bitmap. Sizes: 28 sidebar and Setup rail, 22 tray flyout,
 * 48 first-run empty state, 32 Overlay.
 */
export function Logo({ size = 28, label }: { size?: number; label?: string }) {
  return (
    <svg
      className="vc-logo"
      viewBox="0 0 24 24"
      width={size}
      height={size}
      role={label ? "img" : undefined}
      aria-label={label}
      aria-hidden={label ? undefined : true}
    >
      <path className="vc-logo-tile" d="M12 0C20.6 0 24 3.4 24 12S20.6 24 12 24 0 20.6 0 12 3.4 0 12 0Z" />
      <rect className="vc-logo-bar" x="6.84" y="9.47" width="2.25" height="5.06" rx="1.125" />
      <rect className="vc-logo-bar" x="10.875" y="6" width="2.25" height="12" rx="1.125" />
      <rect className="vc-logo-bar" x="14.9" y="8.16" width="2.25" height="7.69" rx="1.125" />
    </svg>
  );
}

/** Empty and error states: an icon (or the logo) on a tint squircle, never an illustration. */
export function ArtTile({ art }: { art: IconName | "logo" }) {
  if (art === "logo") {
    return (
      <div className="vc-art vc-art-logo">
        <Logo size={48} />
      </div>
    );
  }
  return (
    <div className="vc-art">
      <Icon name={art} size={28} />
    </div>
  );
}
