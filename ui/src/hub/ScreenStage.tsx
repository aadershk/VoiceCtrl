import { useEffect, useLayoutEffect, useRef, useState } from "react";
import type { ReactNode } from "react";
import { cascade, exit, prefersReducedMotion } from "../design/motion";
import { rowDomId } from "./screens";
import type { ScreenId } from "./screens";

/** Where the Hub is headed. `seq` makes a repeat of the same place a new request. */
export interface NavTarget {
  screen: ScreenId;
  row?: string | undefined;
  seq: number;
}

export interface ScreenStageProps {
  target: NavTarget;
  render: (screen: ScreenId) => ReactNode;
  /** Called once the landing row is on screen, to flash it. */
  onLanded: (row: string) => void;
}

/**
 * The content card: the only scrolling region (DESIGN §5), and the screen change of §10. The old
 * screen exits (clicks are ignored meanwhile), then the new one's blocks cascade in 28 ms apart.
 * A new request during the exit just retargets it. A landing row is scrolled to the centre.
 */
export function ScreenStage({ target, render, onLanded }: ScreenStageProps) {
  const [shown, setShown] = useState(target);
  const scroller = useRef<HTMLDivElement>(null);
  const stage = useRef<HTMLDivElement>(null);
  const lastScreen = useRef<ScreenId | null>(null);

  useEffect(() => {
    if (target.seq === shown.seq) return;
    if (target.screen === shown.screen) {
      setShown(target);
      return;
    }
    const el = stage.current;
    let current = true;
    el?.classList.add("hub-stage-leaving");
    void (el ? exit(el) : Promise.resolve()).then(() => {
      if (current) setShown(target);
    });
    return () => {
      current = false;
    };
  }, [target, shown]);

  const landed = useRef(onLanded);
  landed.current = onLanded;

  useLayoutEffect(() => {
    const el = stage.current;
    const box = scroller.current;
    if (!el || !box) return;
    el.classList.remove("hub-stage-leaving");
    // An exit may have been cut short by a return to this same screen: drop what it left behind.
    for (const animation of el.getAnimations?.() ?? []) animation.cancel();

    const firstShow = lastScreen.current === null;
    const newScreen = lastScreen.current !== shown.screen;
    lastScreen.current = shown.screen;

    const row = shown.row ? document.getElementById(rowDomId(shown.row)) : null;
    if (row) {
      // Within a screen the scroll glides; a new screen starts already in place.
      const glide = !newScreen && !prefersReducedMotion();
      row.scrollIntoView?.({ block: "center", behavior: glide ? "smooth" : "auto" });
    } else if (newScreen) {
      box.scrollTop = 0;
    }
    if (newScreen && !firstShow && el.firstElementChild) cascade(el.firstElementChild.children);
    if (shown.row) landed.current(shown.row);
  }, [shown]);

  return (
    <div ref={scroller} className="hub-scroll vc-scroll">
      <div ref={stage} className="hub-stage" data-screen={shown.screen}>
        {render(shown.screen)}
      </div>
    </div>
  );
}
