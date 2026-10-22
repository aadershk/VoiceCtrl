/**
 * Glide motion (DESIGN §10) as Web Animations helpers.
 *
 * Every value comes from the tokens in tokens.css, read at call time, so reduced motion needs no
 * code path of its own: its token overrides turn movement to 0 px, shape changes to 0 ms and
 * enters/exits into 120/100 ms fades. Only `transform` and `opacity` are animated.
 *
 * All helpers are interruptible: a new animation on an element first freezes whatever Glide
 * animation is running there, then starts from that visual state. Where `Element.animate` is
 * missing (jsdom in unit tests) they do nothing and resolve at once.
 */
import { useEffect, useLayoutEffect, useRef, useState, useSyncExternalStore } from "react";
import type { RefObject } from "react";

const GLIDE = "glide";
const REDUCED_QUERY = "(prefers-reduced-motion: reduce)";

type Corner = "top left" | "top right" | "bottom left" | "bottom right" | "top center" | "bottom center";

/** The §10 tokens, resolved to numbers and easing strings. */
export interface MotionTokens {
  hover: number;
  fade: number;
  enter: number;
  exit: number;
  shape: number;
  nudge: number;
  move: number;
  lift: number;
  scaleFrom: number;
  scaleTo: number;
  stagger: number;
  easeOut: string;
  easeIn: string;
  easeNudge: string;
  easeSpring: string;
}

function cssVar(style: CSSStyleDeclaration, name: string): string {
  return style.getPropertyValue(name).trim();
}

function ms(value: string, fallback: number): number {
  const n = Number.parseFloat(value);
  if (Number.isNaN(n)) return fallback;
  return value.endsWith("ms") ? n : value.endsWith("s") ? n * 1000 : n;
}

function num(value: string, fallback: number): number {
  const n = Number.parseFloat(value);
  return Number.isNaN(n) ? fallback : n;
}

/** Reads the motion tokens from the document root. Fallbacks are the full-motion values. */
export function readMotion(root: Element = document.documentElement): MotionTokens {
  const s = getComputedStyle(root);
  return {
    hover: ms(cssVar(s, "--dur-hover"), 120),
    fade: ms(cssVar(s, "--dur-fade"), 220),
    enter: ms(cssVar(s, "--dur-enter"), 280),
    exit: ms(cssVar(s, "--dur-exit"), 180),
    shape: ms(cssVar(s, "--dur-shape"), 420),
    nudge: ms(cssVar(s, "--dur-nudge"), 520),
    move: num(cssVar(s, "--move"), 10),
    lift: num(cssVar(s, "--lift"), 5),
    scaleFrom: num(cssVar(s, "--scale-from"), 0.94),
    scaleTo: num(cssVar(s, "--scale-to"), 0.97),
    stagger: ms(cssVar(s, "--stagger"), 28),
    easeOut: cssVar(s, "--ease-out") || "cubic-bezier(.2, .8, .2, 1)",
    easeIn: cssVar(s, "--ease-in") || "cubic-bezier(.4, 0, 1, 1)",
    easeNudge: cssVar(s, "--ease-nudge") || "cubic-bezier(.34, 1.56, .64, 1)",
    easeSpring: cssVar(s, "--ease-spring") || "cubic-bezier(.2, .8, .2, 1)",
  };
}

/** True when Windows' animation effects are off (media query) or the host says so. */
export function prefersReducedMotion(): boolean {
  if (document.documentElement.dataset.reducedMotion === "true") return true;
  return typeof matchMedia === "function" && matchMedia(REDUCED_QUERY).matches;
}

function subscribeReducedMotion(onChange: () => void): () => void {
  const media = typeof matchMedia === "function" ? matchMedia(REDUCED_QUERY) : null;
  media?.addEventListener("change", onChange);
  const observer = new MutationObserver(onChange);
  observer.observe(document.documentElement, { attributes: true, attributeFilter: ["data-reduced-motion"] });
  return () => {
    media?.removeEventListener("change", onChange);
    observer.disconnect();
  };
}

/** Follows reduced motion live, for components that switch behaviour (e.g. the level meter). */
export function useReducedMotion(): boolean {
  return useSyncExternalStore(subscribeReducedMotion, prefersReducedMotion, () => false);
}

/** §10 rule 7: true until two frames after the first paint. */
export function isFirstPaint(): boolean {
  return document.documentElement.hasAttribute("data-first-paint");
}

/** Lets transitions and animations run once the first frame is on screen. Call once per page. */
export function markFirstPaintDone(): void {
  requestAnimationFrame(() =>
    requestAnimationFrame(() => document.documentElement.removeAttribute("data-first-paint")),
  );
}

function canAnimate(el: Element): el is HTMLElement {
  return typeof (el as HTMLElement).animate === "function" && !isFirstPaint();
}

/** Freezes any running Glide animation at its current frame so the next one starts from there. */
function takeOver(el: HTMLElement): void {
  for (const running of el.getAnimations()) {
    if (running.id !== GLIDE) continue;
    try {
      running.commitStyles();
    } catch {
      // commitStyles throws on elements that aren't rendered; the jump is then invisible anyway.
    }
    running.cancel();
  }
}

function run(el: HTMLElement, keyframes: Keyframe[], options: KeyframeAnimationOptions): Animation {
  takeOver(el);
  const animation = el.animate(keyframes, { ...options, id: GLIDE });
  // Drop what takeOver committed, so the element rests on its stylesheet values again.
  animation.finished.then(
    () => {
      el.style.removeProperty("opacity");
      el.style.removeProperty("transform");
    },
    () => undefined,
  );
  return animation;
}

async function settle(animation: Animation): Promise<void> {
  try {
    await animation.finished;
  } catch {
    // Cancelled by a newer animation: the newer one owns the element now.
  }
}

/** Enter (§10): fade in and rise `--move` on `--dur-enter`. */
export function enter(el: Element, delay = 0): void {
  if (!canAnimate(el)) return;
  const t = readMotion();
  run(el, [
    { opacity: 0, transform: `translateY(${t.move}px)` },
    { opacity: 1, transform: "none" },
  ], { duration: t.enter, delay, easing: t.easeOut, fill: "backwards" });
}

/** Screen blocks cascade in `--stagger` apart (§10 screen change). */
export function cascade(blocks: Iterable<Element>): void {
  const t = readMotion();
  let i = 0;
  for (const block of blocks) enter(block, Math.min(i++, 4) * t.stagger);
}

/** Exit (§10): fade out and lift `--lift` on `--dur-exit`. `drop` moves down instead (Overlay Cancel). */
export async function exit(el: Element, options: { drop?: number } = {}): Promise<void> {
  if (!canAnimate(el)) return;
  const t = readMotion();
  const dy = options.drop !== undefined ? (t.move === 0 ? 0 : options.drop) : -t.lift;
  await settle(run(el, [{ opacity: 0, transform: `translateY(${dy}px)` }], {
    duration: t.exit,
    easing: t.easeIn,
    fill: "forwards",
  }));
}

/** Popover enter: scale `--scale-from` → 1 with a `--lift` rise, growing from the opener's corner. */
export function popoverEnter(el: Element, origin: Corner = "top left"): void {
  if (!canAnimate(el)) return;
  const t = readMotion();
  el.style.transformOrigin = origin;
  run(el, [
    { opacity: 0, transform: `translateY(${t.lift}px) scale(${t.scaleFrom})` },
    { opacity: 1, transform: "none" },
  ], { duration: t.enter, easing: t.easeOut, fill: "backwards" });
}

/** Popover exit: to `--scale-to` and transparent on `--dur-exit`. */
export async function popoverExit(el: Element): Promise<void> {
  if (!canAnimate(el)) return;
  const t = readMotion();
  await settle(run(el, [{ opacity: 0, transform: `scale(${t.scaleTo})` }], {
    duration: t.exit,
    easing: t.easeIn,
    fill: "forwards",
  }));
}

/** Scrims and content swaps: opacity only, on `--dur-fade`. */
export function fadeIn(el: Element): void {
  if (!canAnimate(el)) return;
  const t = readMotion();
  run(el, [{ opacity: 0 }, { opacity: 1 }], { duration: t.fade, easing: t.easeOut, fill: "backwards" });
}

export async function fadeOut(el: Element): Promise<void> {
  if (!canAnimate(el)) return;
  const t = readMotion();
  await settle(run(el, [{ opacity: 0 }], { duration: t.exit, easing: t.easeIn, fill: "forwards" }));
}

/** The one overshoot (§1, §10): the Failed Dictation notice. */
export function nudge(el: Element): void {
  if (!canAnimate(el)) return;
  const t = readMotion();
  const from = t.scaleFrom === 1 ? 1 : 0.92;
  run(el, [
    { opacity: 0, transform: `scale(${from})` },
    { opacity: 1, transform: "none" },
  ], { duration: t.nudge, easing: from === 1 ? t.easeOut : t.easeNudge, fill: "backwards" });
}

/** A deleted row exits 15 px to the right (§10 lists); the caller removes it afterwards. */
export async function exitRow(el: Element): Promise<void> {
  if (!canAnimate(el)) return;
  const t = readMotion();
  const dx = t.move === 0 ? 0 : 15;
  await settle(run(el, [{ opacity: 0, transform: `translateX(${dx}px)` }], {
    duration: t.exit,
    easing: t.easeIn,
    fill: "forwards",
  }));
}

/**
 * FLIP for lists (§10): children marked `data-flip-key` slide from their previous place to the
 * new one on the spring; new children fade in, starting a quarter of the way through.
 * Returns the positions to pass into the next call.
 */
export function playFlip(container: Element, previous: ReadonlyMap<string, DOMRect>): Map<string, DOMRect> {
  const items = Array.from(container.querySelectorAll<HTMLElement>(":scope > [data-flip-key]"));
  const now = new Map<string, DOMRect>();
  for (const item of items) now.set(item.dataset.flipKey ?? "", item.getBoundingClientRect());
  if (previous.size === 0) return now;

  const t = readMotion();
  for (const item of items) {
    if (!canAnimate(item)) continue;
    const before = previous.get(item.dataset.flipKey ?? "");
    const after = now.get(item.dataset.flipKey ?? "");
    if (!after) continue;
    if (!before) {
      run(item, [{ opacity: 0 }, { opacity: 1 }], {
        duration: Math.max(t.shape * 0.75, t.enter),
        delay: t.shape * 0.25,
        easing: t.easeOut,
        fill: "backwards",
      });
      continue;
    }
    const dx = before.left - after.left;
    const dy = before.top - after.top;
    if (t.shape === 0 || (dx === 0 && dy === 0)) continue;
    run(item, [
      { transform: `translate(${dx}px, ${dy}px)` },
      { transform: "none" },
    ], { duration: t.shape, easing: t.easeSpring });
  }
  return now;
}

/** Runs FLIP on a list container whenever `deps` change. Mounting never animates. */
export function useFlipList(containerRef: RefObject<HTMLElement | null>, deps: readonly unknown[]): void {
  const positions = useRef<Map<string, DOMRect>>(new Map());
  useLayoutEffect(() => {
    const container = containerRef.current;
    if (container) positions.current = playFlip(container, positions.current);
    // The caller names what reorders the list, so `deps` is deliberately the dependency list.
  }, deps);
}

export interface PresenceAnimations<T extends HTMLElement> {
  enter: (el: T) => void;
  exit: (el: T) => Promise<void>;
}

/**
 * Keeps an element mounted while its exit animation plays. Something already open at mount
 * appears without animating (§10 rule 7); reopening during an exit retargets from mid-flight.
 */
export function usePresence<T extends HTMLElement>(
  open: boolean,
  animations: PresenceAnimations<T>,
): { mounted: boolean; ref: RefObject<T | null> } {
  const [mounted, setMounted] = useState(open);
  const ref = useRef<T | null>(null);
  const openedAtMount = useRef(open);
  const latest = useRef(animations);
  latest.current = animations;

  useEffect(() => {
    if (open) {
      setMounted(true);
      return;
    }
    const el = ref.current;
    if (!el) {
      setMounted(false);
      return;
    }
    let current = true;
    void latest.current.exit(el).then(() => {
      if (current) setMounted(false);
    });
    return () => {
      current = false;
    };
  }, [open]);

  useLayoutEffect(() => {
    if (!open || !mounted || !ref.current) return;
    if (openedAtMount.current) {
      openedAtMount.current = false;
      return;
    }
    latest.current.enter(ref.current);
  }, [open, mounted]);

  return { mounted, ref };
}
