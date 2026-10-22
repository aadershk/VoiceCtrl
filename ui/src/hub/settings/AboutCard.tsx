import { Button, Logo } from "../../design/components";
import { GITHUB_URL, openLink, releaseUrl } from "../links";

/** The About card at the end of Settings (DESIGN §12), in place of the old footer line. */
export function AboutCard({ version }: { version: string }) {
  return (
    <section className="hub-about" aria-label="About VoiceCtrl">
      <Logo size={28} />
      <div className="hub-about-text">
        <b>VoiceCtrl {version}</b>
        <span>Free and open source, MIT licence</span>
      </div>
      <Button variant="ghost" size="sm" trailingIcon="external" onClick={() => openLink(releaseUrl(version))}>
        What's new
      </Button>
      <Button variant="ghost" size="sm" trailingIcon="external" onClick={() => openLink(GITHUB_URL)}>
        GitHub
      </Button>
    </section>
  );
}
