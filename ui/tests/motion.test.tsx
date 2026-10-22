import { act, render, screen } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import {
  enter,
  exit,
  isFirstPaint,
  markFirstPaintDone,
  playFlip,
  popoverExit,
  prefersReducedMotion,
  readMotion,
  usePresence,
} from "../src/design/motion";

describe("motion tokens", () => {
  it("reads durations in ms or s and falls back to full motion", () => {
    const root = document.documentElement;
    root.style.setProperty("--dur-enter", "0.3s");
    root.style.setProperty("--dur-shape", "0ms");
    const t = readMotion();
    expect(t.enter).toBe(300);
    expect(t.shape).toBe(0);
    expect(t.exit).toBe(180);
    root.style.removeProperty("--dur-enter");
    root.style.removeProperty("--dur-shape");
  });

  it("follows the host's data-reduced-motion attribute", () => {
    expect(prefersReducedMotion()).toBe(false);
    document.documentElement.dataset.reducedMotion = "true";
    expect(prefersReducedMotion()).toBe(true);
  });
});

describe("first paint", () => {
  it("lifts the guard two frames after the first paint", async () => {
    document.documentElement.setAttribute("data-first-paint", "");
    const frames: FrameRequestCallback[] = [];
    vi.spyOn(window, "requestAnimationFrame").mockImplementation((cb) => frames.push(cb));
    markFirstPaintDone();
    frames.shift()?.(0);
    expect(isFirstPaint()).toBe(true);
    frames.shift()?.(16);
    expect(isFirstPaint()).toBe(false);
    vi.restoreAllMocks();
  });
});

describe("helpers without Web Animations (jsdom)", () => {
  it("do nothing and resolve at once", async () => {
    const el = document.createElement("div");
    expect(() => enter(el)).not.toThrow();
    await expect(exit(el)).resolves.toBeUndefined();
    await expect(popoverExit(el)).resolves.toBeUndefined();
  });
});

describe("FLIP", () => {
  it("records positions without animating the first time", () => {
    const list = document.createElement("div");
    for (const key of ["a", "b"]) {
      const row = document.createElement("div");
      row.dataset.flipKey = key;
      row.animate = vi.fn();
      list.append(row);
    }
    const positions = playFlip(list, new Map());
    expect([...positions.keys()]).toEqual(["a", "b"]);
    for (const row of list.children) expect((row as HTMLElement).animate).not.toHaveBeenCalled();
  });
});

function Presence({ exitDone }: { exitDone: Promise<void> }) {
  const [open, setOpen] = useState(true);
  const { mounted, ref } = usePresence<HTMLDivElement>(open, { enter: () => undefined, exit: () => exitDone });
  return (
    <>
      <button onClick={() => setOpen((o) => !o)}>toggle</button>
      {mounted && <div ref={ref}>panel</div>}
    </>
  );
}

describe("usePresence", () => {
  it("keeps the element mounted until its exit animation finishes", async () => {
    let finish = () => undefined as void;
    const exitDone = new Promise<void>((resolve) => {
      finish = resolve;
    });
    render(<Presence exitDone={exitDone} />);
    act(() => screen.getByText("toggle").click());
    expect(screen.queryByText("panel")).not.toBeNull();
    await act(async () => {
      finish();
      await exitDone;
    });
    expect(screen.queryByText("panel")).toBeNull();
  });
});
