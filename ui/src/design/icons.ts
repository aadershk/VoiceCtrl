/**
 * The VoiceCtrl icon set (DESIGN §7): one stroke family on a 24 grid, round caps and joins,
 * currentColor, no fills. Every path is copied verbatim from the approved boards in
 * .scratch/v4-overhaul/prototypes (Hub: 10-hub/gen-hub2.js, tray and Settings rows:
 * ved-24-tray-settings/index.html, Setup: 13-setup, Overlay: 11-overlay/Mini). No icon library.
 *
 * The values are static SVG child markup, so rendering them with innerHTML is safe and keeps
 * one source for both the React surfaces and the framework-free Overlay page.
 */
export const icons = {
  // Hub navigation
  dictations: '<path d="M4 6h16M4 12h16M4 18h10"/>',
  dictionary: '<path d="M5 4.5A1.5 1.5 0 0 1 6.5 3H19v15H6.5A1.5 1.5 0 0 0 5 19.5z"/><path d="M5 19.5A1.5 1.5 0 0 0 6.5 21H19"/>',
  snippets: '<path d="M8 5H6.5A1.5 1.5 0 0 0 5 6.5V10l-1.5 2L5 14v3.5A1.5 1.5 0 0 0 6.5 19H8M16 5h1.5A1.5 1.5 0 0 1 19 6.5V10l1.5 2-1.5 2v3.5a1.5 1.5 0 0 1-1.5 1.5H16"/>',
  style: '<path d="M3.5 18L8 6l4.5 12M5.2 13.5h5.6"/><circle cx="17.5" cy="15.5" r="2.5"/><path d="M20 12.5V18"/>',
  profiles: '<rect x="3.5" y="4.5" width="17" height="15" rx="2.5"/><path d="M3.5 9h17"/>',
  settings: '<path d="M4 7h10M18 7h2M4 17h4M12 17h8"/><circle cx="16" cy="7" r="2"/><circle cx="10" cy="17" r="2"/>',

  // Actions
  search: '<circle cx="11" cy="11" r="6.5"/><path d="M20 20l-4.2-4.2"/>',
  copy: '<rect x="8.5" y="8.5" width="11" height="11" rx="2"/><path d="M15.5 8.5V6a1.5 1.5 0 0 0-1.5-1.5H6A1.5 1.5 0 0 0 4.5 6v8A1.5 1.5 0 0 0 6 15.5h2.5"/>',
  paste: '<rect x="6" y="4.5" width="12" height="16" rx="2"/><path d="M9.5 4.5V3.5h5v1M12 9v6M9.5 12.5L12 15l2.5-2.5"/>',
  delete: '<path d="M4.5 7h15M10 4h4M6.5 7l.8 12a1.5 1.5 0 0 0 1.5 1.4h6.4a1.5 1.5 0 0 0 1.5-1.4l.8-12"/>',
  edit: '<path d="M4.5 19.5l1-4L15 6l3 3-9.5 9.5z"/><path d="M13 8l3 3"/>',
  retry: '<path d="M4.5 12a7.5 7.5 0 1 0 2.2-5.3"/><path d="M4.5 4.5v3.5H8"/>',
  close: '<path d="M6 6l12 12M18 6L6 18"/>',
  check: '<path d="M5 12.5l4.5 4.5L19 7.5"/>',
  plus: '<path d="M12 5v14M5 12h14"/>',
  import: '<path d="M12 4v11M8 11l4 4 4-4M5 16v2.5A1.5 1.5 0 0 0 6.5 20h11a1.5 1.5 0 0 0 1.5-1.5V16"/>',
  export: '<path d="M12 15V4M8 8l4-4 4 4M5 16v2.5A1.5 1.5 0 0 0 6.5 20h11a1.5 1.5 0 0 0 1.5-1.5V16"/>',
  download: '<path d="M12 4v12M7 11l5 5 5-5M5 20h14"/>',
  more: '<circle cx="6" cy="12" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="18" cy="12" r="1"/>',
  external: '<path d="M14 5h5v5M19 5l-8 8M17 14v4.5a1.5 1.5 0 0 1-1.5 1.5h-10A1.5 1.5 0 0 1 4 18.5v-10A1.5 1.5 0 0 1 5.5 7H10"/>',
  arrowRight: '<path d="M5 12h14M13 6l6 6-6 6"/>',
  chevronDown: '<path d="M7 10l5 5 5-5"/>',
  chevronUp: '<path d="M7 14l5-5 5 5"/>',

  // Status and objects
  alert: '<circle cx="12" cy="12" r="8.5"/><path d="M12 8v4.5M12 15.8h.01"/>',
  micOff: '<path d="M9 9v2.5a3 3 0 0 0 5.1 2.1M15 10V6.5a3 3 0 0 0-5.8-1M5.5 11.5a6.5 6.5 0 0 0 10.4 5.2M18.5 11.5a6.5 6.5 0 0 1-.4 2.2M12 18v2.5M4 4l16 16"/>',
  key: '<circle cx="8" cy="15" r="3.5"/><path d="M10.5 12.5L19 4M16 7l2 2M14 9l1.5 1.5"/>',
  sparkle: '<path d="M12 4v4M12 16v4M4 12h4M16 12h4M6.5 6.5l2.5 2.5M15 15l2.5 2.5M17.5 6.5L15 9M9 15l-2.5 2.5"/>',
  moon: '<path d="M19.5 14.5A8 8 0 0 1 9.5 4.5a8 8 0 1 0 10 10z"/>',
  sun: '<circle cx="12" cy="12" r="4"/><path d="M12 2.5v2M12 19.5v2M2.5 12h2M19.5 12h2M5.3 5.3l1.4 1.4M17.3 17.3l1.4 1.4M5.3 18.7l1.4-1.4M17.3 6.7l1.4-1.4"/>',
  gift: '<rect x="4" y="8.5" width="16" height="11" rx="2"/><path d="M12 8.5v11M4 12.5h16M12 8.5c-1-3-5-3.5-5-1s5 1 5 1zm0 0c1-3 5-3.5 5-1s-5 1-5 1z"/>',
  mail: '<rect x="3.5" y="5.5" width="17" height="13" rx="2"/><path d="M4 7l8 6 8-6"/>',
  cloud: '<path d="M7 18.5a4.5 4.5 0 0 1-.6-9 6 6 0 0 1 11.4 1.6A3.8 3.8 0 0 1 17.5 18.5z"/>',
  chip: '<rect x="6.5" y="6.5" width="11" height="11" rx="2"/><path d="M9.5 3.5v3M14.5 3.5v3M9.5 17.5v3M14.5 17.5v3M3.5 9.5h3M3.5 14.5h3M17.5 9.5h3M17.5 14.5h3"/>',
  wifi: '<path d="M3.5 9.5a12 12 0 0 1 17 0M6.5 12.8a7.5 7.5 0 0 1 11 0M9.5 16a3.2 3.2 0 0 1 5 0"/><path d="M12 19h.01"/>',
  volume: '<path d="M4.5 9.5h3l4.5-4v13l-4.5-4h-3z"/><path d="M15.5 9a4 4 0 0 1 0 6M18 6.5a7.5 7.5 0 0 1 0 11"/>',

  // Tray flyout (VED-24)
  window: '<rect x="3.5" y="4.5" width="17" height="15" rx="2.5"/><path d="M3.5 9h17"/>',
  pause: '<circle cx="12" cy="12" r="8.5"/><path d="M10 9v6M14 9v6"/>',
  play: '<circle cx="12" cy="12" r="8.5"/><path d="M10.2 8.8l5 3.2-5 3.2z"/>',
  power: '<path d="M12 4v7.5"/><path d="M7.4 7a7 7 0 1 0 9.2 0"/>',

  // Frameless window controls (§5), drawn at 14 px
  minimize: '<path d="M5 12h14"/>',
  maximize: '<rect x="5.5" y="5.5" width="13" height="13" rx="2"/>',
  // Not on a board: the maximize square, stepped back behind a smaller one (Hub frame slice).
  restore: '<rect x="5.5" y="8.5" width="10" height="10" rx="2"/><path d="M8.5 8.5V7A1.5 1.5 0 0 1 10 5.5h7A1.5 1.5 0 0 1 18.5 7v7a1.5 1.5 0 0 1-1.5 1.5h-3"/>',

  // Overlay variants (Mini board): slightly larger circle and mic, drawn at 14 px with stroke 1.8
  overlayAlert: '<circle cx="12" cy="12" r="9"/><path d="M12 7.5v5M12 16.2v.1"/>',
  // Hands-free lock (VED-74): a padlock, shown beside the Listening bars while the recording is locked on.
  overlayLock: '<rect x="5.5" y="11" width="13" height="9.5" rx="2.5"/><path d="M8.5 11V8a3.5 3.5 0 0 1 7 0v3"/>',
  overlayMicOff: '<path d="M9 9v2a3 3 0 0 0 5.1 2.1M15 10V6a3 3 0 0 0-5.7-1.3M5 11a7 7 0 0 0 11.5 5.4M19 11a7 7 0 0 1-.4 2.3M12 18v3M4 4l16 16"/>',
} as const satisfies Record<string, string>;

export type IconName = keyof typeof icons;

/** Icon sizes from §7: 18 default, 16 small buttons and menus, 14 Overlay and window controls. */
export type IconSize = 14 | 16 | 18 | 28;

export const iconNames = Object.keys(icons) as IconName[];

/**
 * A complete, decorative `<svg>` string for the framework-free Overlay page.
 * The stroke styling comes from the `vc-icon` class in components.css.
 */
export function iconMarkup(name: IconName, size: IconSize = 18): string {
  return `<svg class="vc-icon vc-icon-${size}" viewBox="0 0 24 24" aria-hidden="true" focusable="false">${icons[name]}</svg>`;
}
