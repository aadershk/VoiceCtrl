import { act, fireEvent, render, screen } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import {
  Dialog,
  IconButton,
  Menu,
  MenuAnchor,
  MenuItem,
  NavList,
  ProgressBar,
  RadioCards,
  Segmented,
  Switch,
  TextField,
  Toast,
  TOAST_MS,
  TOAST_WITH_ACTION_MS,
} from "../src/design/components";

const MODES = [
  { value: "auto", label: "Auto" },
  { value: "online", label: "Online" },
  { value: "offline", label: "Offline" },
] as const;

describe("Switch", () => {
  it("is a named switch that flips on click", () => {
    const onChange = vi.fn();
    render(<Switch checked={false} onChange={onChange} label="Sound Cues" />);
    const control = screen.getByRole("switch", { name: "Sound Cues" });
    expect(control.getAttribute("aria-checked")).toBe("false");
    fireEvent.click(control);
    expect(onChange).toHaveBeenCalledWith(true);
  });
});

function ModePicker() {
  const [mode, setMode] = useState<(typeof MODES)[number]["value"]>("auto");
  return <Segmented options={MODES} value={mode} onChange={setMode} label="Mode" />;
}

describe("Segmented", () => {
  it("is a radio group with one tab stop, and arrow keys move the selection", () => {
    render(<ModePicker />);
    const [auto, online, offline] = screen.getAllByRole("radio");
    expect(auto!.tabIndex).toBe(0);
    expect(online!.tabIndex).toBe(-1);
    fireEvent.keyDown(auto!, { key: "ArrowRight" });
    expect(online!.getAttribute("aria-checked")).toBe("true");
    expect(document.activeElement).toBe(online);
    fireEvent.keyDown(online!, { key: "ArrowLeft" });
    fireEvent.keyDown(auto!, { key: "ArrowLeft" });
    expect(offline!.getAttribute("aria-checked")).toBe("true");
  });
});

describe("RadioCards", () => {
  it("marks the chosen card checked", () => {
    render(
      <RadioCards
        label="Mode"
        value="offline"
        onChange={() => undefined}
        options={[
          { value: "online", label: "Online", icon: "cloud", description: "Gemini" },
          { value: "offline", label: "Offline", icon: "chip", description: "On this PC" },
        ]}
      />,
    );
    expect(screen.getByRole("radio", { name: /Offline/ }).getAttribute("aria-checked")).toBe("true");
    expect(screen.getByRole("radio", { name: /Online/ }).getAttribute("aria-checked")).toBe("false");
  });
});

describe("TextField", () => {
  it("links its error message to the input and marks it invalid", () => {
    render(<TextField label="Gemini API key" error="That key didn't work." />);
    const input = screen.getByLabelText("Gemini API key");
    expect(input.getAttribute("aria-invalid")).toBe("true");
    const message = document.getElementById(input.getAttribute("aria-describedby")!);
    expect(message?.textContent).toContain("That key didn't work.");
  });
});

describe("IconButton and ProgressBar", () => {
  it("name themselves for screen readers", () => {
    render(
      <>
        <IconButton icon="copy" label="Copy" />
        <ProgressBar value={0.62} label="Downloading 4.1" />
      </>,
    );
    expect(screen.getByRole("button", { name: "Copy" })).toBeTruthy();
    expect(screen.getByRole("progressbar", { name: "Downloading 4.1" }).getAttribute("aria-valuenow")).toBe("62");
  });
});

describe("NavList", () => {
  it("marks the current screen and moves the one highlight to it", () => {
    const items = [
      { id: "a", label: "Dictations", icon: "dictations" },
      { id: "b", label: "Dictionary", icon: "dictionary" },
      { id: "c", label: "Snippets", icon: "snippets" },
    ] as const;
    const { container } = render(<NavList items={items} current="c" onSelect={() => undefined} label="Hub" />);
    expect(screen.getByRole("button", { name: "Snippets" }).getAttribute("aria-current")).toBe("page");
    const highlight = container.querySelector<HTMLElement>(".vc-nav-highlight");
    expect(highlight?.style.transform).toBe("translateY(84px)");
  });
});

function MenuHarness({ onPick }: { onPick: () => void }) {
  const [open, setOpen] = useState(false);
  return (
    <MenuAnchor>
      <button onClick={() => setOpen(true)}>More</button>
      <Menu open={open} onClose={() => setOpen(false)} label="More">
        <MenuItem icon="copy" onSelect={onPick}>Copy all as text</MenuItem>
        <MenuItem icon="delete" disabled onSelect={() => undefined}>Nothing to clear</MenuItem>
        <MenuItem icon="delete" danger onSelect={() => undefined}>Clear all Dictations</MenuItem>
      </Menu>
    </MenuAnchor>
  );
}

describe("Menu", () => {
  it("focuses the first item, skips disabled ones with the arrows, and closes on Esc", async () => {
    render(<MenuHarness onPick={() => undefined} />);
    const opener = screen.getByText("More");
    opener.focus();
    fireEvent.click(opener);
    const items = screen.getAllByRole("menuitem");
    expect(document.activeElement).toBe(items[0]);
    fireEvent.keyDown(items[0]!, { key: "ArrowDown" });
    expect(document.activeElement?.textContent).toBe("Clear all Dictations");
    await act(async () => {
      fireEvent.keyDown(document.activeElement!, { key: "Escape" });
    });
    expect(screen.queryByRole("menu")).toBeNull();
    expect(document.activeElement).toBe(opener);
  });

  it("closes after a pick and on a click outside", async () => {
    const onPick = vi.fn();
    render(<MenuHarness onPick={onPick} />);
    fireEvent.click(screen.getByText("More"));
    await act(async () => {
      fireEvent.click(screen.getByRole("menuitem", { name: "Copy all as text" }));
    });
    expect(onPick).toHaveBeenCalledOnce();
    expect(screen.queryByRole("menu")).toBeNull();

    fireEvent.click(screen.getByText("More"));
    await act(async () => {
      fireEvent.pointerDown(document.body);
    });
    expect(screen.queryByRole("menu")).toBeNull();
  });
});

describe("Dialog", () => {
  it("focuses Cancel by default and cancels on Esc", () => {
    const onCancel = vi.fn();
    render(
      <Dialog open title="Clear all Dictations?" confirmLabel="Clear all" onConfirm={() => undefined} onCancel={onCancel}>
        This can't be undone.
      </Dialog>,
    );
    expect(screen.getByRole("dialog", { name: "Clear all Dictations?" })).toBeTruthy();
    expect(document.activeElement?.textContent).toBe("Cancel");
    fireEvent.keyDown(document.activeElement!, { key: "Escape" });
    expect(onCancel).toHaveBeenCalledOnce();
  });
});

describe("Toast", () => {
  it("closes itself after 3.2 s, or 5 s when it carries Undo", () => {
    vi.useFakeTimers();
    const plain = vi.fn();
    const withUndo = vi.fn();
    render(
      <>
        <Toast open message="Copied" onClose={plain} />
        <Toast open message="Word deleted" action={{ label: "Undo", onClick: () => undefined }} onClose={withUndo} />
      </>,
    );
    act(() => vi.advanceTimersByTime(TOAST_MS));
    expect(plain).toHaveBeenCalledOnce();
    expect(withUndo).not.toHaveBeenCalled();
    act(() => vi.advanceTimersByTime(TOAST_WITH_ACTION_MS - TOAST_MS));
    expect(withUndo).toHaveBeenCalledOnce();
    vi.useRealTimers();
  });
});
