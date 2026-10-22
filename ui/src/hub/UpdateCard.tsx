import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { Button, Icon, IconButton } from "../design/components";
import { enter, isFirstPaint } from "../design/motion";

export interface UpdateCardProps {
  version: string;
  /** ✕ */
  onDismiss: () => void;
  /** Opens the release on GitHub; the card goes too. */
  onWhatsNew: () => void;
}

/**
 * "Updated to {version}" (DESIGN §12): the sidebar card above the status card after a quiet
 * update was installed. It enters with the standard enter once the Hub's first frame is up, so it
 * is noticed without flashing in with the page (§10 rule 7).
 */
export function UpdateCard({ version, onDismiss, onWhatsNew }: UpdateCardProps) {
  const ref = useRef<HTMLElement>(null);
  const [shown, setShown] = useState(() => !isFirstPaint());

  useEffect(() => {
    if (shown) return;
    let frame = 0;
    const wait = () => {
      if (isFirstPaint()) frame = requestAnimationFrame(wait);
      else setShown(true);
    };
    frame = requestAnimationFrame(wait);
    return () => cancelAnimationFrame(frame);
  }, [shown]);

  useLayoutEffect(() => {
    if (shown && ref.current) enter(ref.current);
  }, [shown]);

  if (!shown) return null;
  return (
    <section ref={ref} className="hub-updated" aria-label={`Updated to ${version}`}>
      <div className="hub-updated-head">
        <Icon name="gift" size={16} />
        <div className="hub-updated-text">
          <span className="hub-updated-title">Updated to {version}</span>
          <span className="hub-updated-hint">Installed when VoiceCtrl started</span>
        </div>
        <IconButton icon="close" label="Dismiss" size="sm" className="hub-updated-close" onClick={onDismiss} />
      </div>
      <Button size="sm" block trailingIcon="external" onClick={onWhatsNew}>
        See what's new
      </Button>
    </section>
  );
}
