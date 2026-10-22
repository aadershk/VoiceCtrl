import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { createBridge } from "../src/bridge/bridge";
import { createMockHost } from "../src/bridge/mock";
import { Hub } from "../src/hub/Hub";
import { installProfilesMock } from "../src/hub/mockProfiles";
import { installStyleMock } from "../src/hub/mockStyle";
import type { StyleScenario } from "../src/hub/mockStyle";
import { searchQuickFind } from "../src/hub/findIndex";

// Two macrotasks: the mock answers a command in one, and events it raises arrive in the next.
const settle = async () => {
  await act(() => new Promise((resolve) => setTimeout(resolve, 0)));
  await act(() => new Promise((resolve) => setTimeout(resolve, 0)));
};

async function openStyle(scenario: StyleScenario = {}) {
  const host = createMockHost();
  installStyleMock(host, scenario);
  installProfilesMock(host);
  const bridge = createBridge(host.transport);
  const ready = await bridge.ready();
  render(<Hub bridge={bridge} snapshot={ready} />);
  fireEvent.click(within(screen.getByRole("navigation", { name: "Hub" })).getByRole("button", { name: "Style" }));
  await settle();
  return host;
}

async function click(element: HTMLElement) {
  fireEvent.click(element);
  await settle();
}

const card = (name: string) => within(screen.getByRole("region", { name }));
const toast = () => screen.getByRole("status");

describe("Style screen", () => {
  it("shows a card per kind of app, with the owner's defaults", async () => {
    await openStyle();

    for (const [name, style] of [
      ["Personal chat", "Casual"],
      ["Work chat", "Casual"],
      ["Email", "Formal"],
      ["Documents", "Formal"],
      ["Other apps", "Formal"],
    ] as const) {
      expect(card(name).getByRole("radio", { name: style }).getAttribute("aria-checked")).toBe("true");
    }
    // AI tools and code keep the words as said: no Style to choose.
    expect(card("AI tools and code").queryByRole("radiogroup")).toBeNull();
  });

  it("shows the example in the chosen Style, and changes it with the choice", async () => {
    await openStyle();

    expect(card("Personal chat").getByText(/Yeah, sounds good/).textContent).toBe("Yeah, sounds good. I'll be there in five minutes");

    await click(card("Personal chat").getByRole("radio", { name: "Very casual" }));

    expect(card("Personal chat").getByText(/sounds good/).textContent).toBe("yeah, sounds good. I'll be there in five minutes");
    expect(card("Personal chat").getByText("Starts lowercase. “I” and names keep their capitals.")).toBeTruthy();
  });

  it("lists the apps of each kind as the owner sorted them", async () => {
    await openStyle();

    expect(card("Work chat").getByRole("button", { name: /^Slack:/ })).toBeTruthy();
    expect(card("Work chat").getByRole("button", { name: /^Microsoft Teams:/ })).toBeTruthy();
    expect(card("Personal chat").getByRole("button", { name: /^WhatsApp:/ })).toBeTruthy();
    expect(card("Personal chat").getByRole("button", { name: /^Discord:/ })).toBeTruthy();
  });

  it("moves an app to another kind", async () => {
    await openStyle();

    await click(card("Personal chat").getByRole("button", { name: /^Discord:/ }));
    await click(within(screen.getByRole("menu", { name: "Move Discord to" })).getByRole("menuitem", { name: "Work chat" }));

    expect(toast().textContent).toContain("Moved Discord to Work chat");
    expect(card("Work chat").getByRole("button", { name: /^Discord:/ })).toBeTruthy();
    expect(card("Personal chat").queryByRole("button", { name: /^Discord:/ })).toBeNull();
  });

  it("adds an open app to a card", async () => {
    await openStyle();

    await click(card("Personal chat").getByRole("button", { name: "Add app" }));
    await click(within(screen.getByRole("menu", { name: "Add an open app to Personal chat" })).getByRole("menuitem", { name: /Google Chrome/ }));

    expect(card("Personal chat").getByRole("button", { name: /^Chrome:/ })).toBeTruthy();
  });

  it("saves Your name when the field is left", async () => {
    await openStyle();
    const field = screen.getByRole("textbox", { name: "Your name" }) as HTMLInputElement;

    fireEvent.change(field, { target: { value: "  Alex  " } });
    fireEvent.blur(field);
    await settle();

    expect(field.value).toBe("Alex");
  });

  it("is in Quick Find, Your name included", () => {
    expect(searchQuickFind("style")[0]?.label).toBe("Style");
    expect(searchQuickFind("excited")[0]?.screen).toBe("style");
    expect(searchQuickFind("signature")[0]).toMatchObject({ label: "Your name", screen: "style", row: "your-name" });
  });
});
