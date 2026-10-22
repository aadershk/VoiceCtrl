import type { Theme } from "../bridge/contracts.g";
import { ArtTile, Icon, Logo } from "../design/components";
import { iconNames } from "../design/icons";
import { Section } from "./Section";
import {
  ButtonsSection,
  ChipsSection,
  FieldsSection,
  ListsSection,
  MotionSection,
  NoticesSection,
  PopoversSection,
  SidebarSection,
  TogglesSection,
} from "./sections";

/** The gallery (spec §6): every component in both Themes, side by side, on the mock data. */
export function Gallery() {
  return (
    <main className="g-page">
      <ThemeColumn theme="light" />
      <ThemeColumn theme="dark" />
    </main>
  );
}

function ThemeColumn({ theme }: { theme: Theme }) {
  return (
    <div className="g-theme" data-theme={theme} data-testid={`theme-${theme}`}>
      <p className="g-theme-name">{theme === "light" ? "Light" : "Dark"}</p>
      <TypeSection />
      <ColourSection />
      <IconsSection />
      <ButtonsSection />
      <FieldsSection />
      <TogglesSection />
      <ChipsSection />
      <SidebarSection theme={theme} />
      <ListsSection />
      <NoticesSection />
      <PopoversSection />
      <MotionSection />
    </div>
  );
}

function TypeSection() {
  return (
    <Section title="Type (§4)">
      <h1 className="vc-title">Dictations</h1>
      <h2 className="vc-step-title">Connect your free Gemini key</h2>
      <p className="vc-rail-line">Say it once. It lands where you type.</p>
      <h3 className="vc-dialog-title">Clear all Dictations?</h3>
      <div className="g-row">
        <Logo size={28} />
        <span className="vc-brand">VoiceCtrl</span>
      </div>
      <h4 className="vc-group">Today</h4>
      <p className="vc-serif" style={{ margin: 0 }}>
        “Can we find thirty minutes on Thursday to go through the forecast?”
      </p>
      <p style={{ margin: 0, fontSize: 15 }}>Row text and field text, Public Sans 15.</p>
      <p className="vc-sub">Body, buttons and nav, Public Sans 14 in --ink-2.</p>
      <div className="g-row">
        <span className="vc-meta">9:41 · Outlook</span>
        <span className="vc-caption">Caption 12, tabular 0123456789</span>
      </div>
    </Section>
  );
}

const SWATCHES = [
  "--bg", "--panel", "--tint", "--line", "--line-2", "--hover",
  "--ink", "--ink-2", "--ink-3", "--btn", "--tile", "--coral",
  "--warn-bg", "--warn-ink", "--danger", "--scrim", "--ov-bg", "--ov-line",
] as const;

function ColourSection() {
  return (
    <Section title="Colour (§2)">
      <div className="g-swatches">
        {SWATCHES.map((token) => (
          <span key={token} className="g-swatch">
            <i style={{ background: `var(${token})` }} />
            {token}
          </span>
        ))}
      </div>
    </Section>
  );
}

function IconsSection() {
  return (
    <Section title="Logo and icons (§6, §7)">
      <div className="g-row">
        <Logo size={22} />
        <Logo size={28} />
        <Logo size={32} />
        <ArtTile art="logo" />
        <ArtTile art="dictionary" />
      </div>
      <div className="g-icons">
        {iconNames.map((name) => (
          <span key={name} className="g-icon">
            <Icon name={name} size={name.startsWith("overlay") || name === "minimize" || name === "maximize" ? 14 : 18} />
            {name}
          </span>
        ))}
      </div>
    </Section>
  );
}
