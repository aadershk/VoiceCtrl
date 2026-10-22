import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { createBridge } from "../src/bridge/bridge";
import type { ProfileEntry } from "../src/bridge/contracts.g";
import { createMockHost } from "../src/bridge/mock";
import { Hub } from "../src/hub/Hub";
import { installProfilesMock, SAMPLE_PROFILES } from "../src/hub/mockProfiles";
import type { ProfilesScenario } from "../src/hub/mockProfiles";
import { installTranscriptionMock } from "../src/hub/mockTranscription";
import { cleanupNote, exeName, profileError, profileSummary } from "../src/hub/profileCopy";

// Two macrotasks: the mock answers a command in one, and events it raises arrive in the next.
const settle = async () => {
  await act(() => new Promise((resolve) => setTimeout(resolve, 0)));
  await act(() => new Promise((resolve) => setTimeout(resolve, 0)));
};

async function openProfiles(scenario: ProfilesScenario = {}) {
  const host = createMockHost();
  installProfilesMock(host, scenario);
  installTranscriptionMock(host);
  const bridge = createBridge(host.transport);
  const ready = await bridge.ready();
  render(<Hub bridge={bridge} snapshot={ready} />);
  fireEvent.click(within(screen.getByRole("navigation", { name: "Hub" })).getByRole("button", { name: "Profiles" }));
  await settle();
  return host;
}

const list = () => within(screen.getByRole("region", { name: "Your profiles" }));
const appMenu = () => within(screen.getByRole("menu", { name: "Open apps" }));
const toast = () => screen.getByRole("status");
const outlook = SAMPLE_PROFILES[0] as ProfileEntry;

async function click(element: HTMLElement) {
  fireEvent.click(element);
  await settle();
}

describe("Profiles copy", () => {
  it("sums a Profile up the way the board does", () => {
    expect(profileSummary(outlook)).toBe("Prose · Default cleanup · Email Layout");
    expect(profileSummary({ ...outlook, formatting: "none", cleanup: "verbatim", emailLayout: false })).toBe("No formatting · Word for word");
    expect(profileSummary({ ...outlook, style: "veryCasual", cleanup: "aggressive", emailLayout: false })).toBe(
      "Very casual · Prose · Thorough cleanup",
    );
  });

  it("names the Settings default under Cleanup", () => {
    expect(cleanupNote("standard")).toBe("Word for word keeps every “um” and repeat. Default follows Settings (Standard).");
    expect(cleanupNote("verbatim")).toContain("(Word for word)");
    expect(cleanupNote(null)).toBe("Word for word keeps every “um” and repeat. Default follows Settings.");
  });

  it("puts each error under its field", () => {
    expect(profileError("appMissing", "")).toEqual({ field: "app", message: "Choose the app this Profile is for." });
    expect(profileError("alreadyThere", "Slack")?.message).toBe("Slack already has a Profile. Edit that one instead.");
    expect(profileError("instructionsTooLong", "x")?.field).toBe("instructions");
    expect(profileError("toneTooLong", "x")?.field).toBe("instructions");
    expect(profileError("cleanupInvalid", "x")?.field).toBeNull();
    expect(profileError("none", "x")).toBeNull();
  });

  it("shows the exe as Task Manager does", () => {
    expect(exeName("OUTLOOK")).toBe("OUTLOOK.exe");
    expect(exeName("slack.exe")).toBe("slack.exe");
  });
});

describe("Profiles screen", () => {
  it("lists each Profile by app, with its summary", async () => {
    await openProfiles();

    expect(screen.getByRole("heading", { name: "3 profiles" })).toBeTruthy();
    expect(list().getByText("Outlook")).toBeTruthy();
    expect(list().getByText("Structured · Word for word")).toBeTruthy();
    expect(list().getByText("Very casual · Prose · Light cleanup")).toBeTruthy();
  });

  it("explains Profiles when there are none", async () => {
    await openProfiles({ profiles: [] });

    expect(screen.getByRole("heading", { name: "No Profiles yet" })).toBeTruthy();
    expect(screen.queryByRole("region", { name: "Your profiles" })).toBeNull();
  });

  it("adds a Profile for an open app", async () => {
    await openProfiles();

    await click(screen.getByRole("button", { name: "Add profile" }));
    // Add opens straight onto the open apps; ones with a Profile can't be picked twice.
    expect((appMenu().getByRole("menuitem", { name: /Slack/ }) as HTMLButtonElement).disabled).toBe(true);
    await click(appMenu().getByRole("menuitem", { name: /Word/ }));
    // Word is a document app: its Style follows Documents until one is picked (ticket 56).
    expect(screen.getByRole("radio", { name: "Same as Documents" }).getAttribute("aria-checked")).toBe("true");
    await click(within(screen.getByRole("radiogroup", { name: "Style" })).getByRole("radio", { name: "Formal" }));
    fireEvent.change(screen.getByRole("textbox", { name: "How I write here" }), { target: { value: "Short paragraphs" } });
    await click(within(screen.getByRole("radiogroup", { name: "Cleanup" })).getByRole("radio", { name: "Word for word" }));
    await click(screen.getByRole("switch", { name: "Email Layout" }));
    await click(screen.getByRole("button", { name: "Save profile" }));

    expect(toast().textContent).toContain("Saved the Word profile");
    // Word starts from what it gets without a Profile: Paragraphs (ticket 55).
    expect(list().getByText("Formal · Paragraphs · Word for word · Email Layout")).toBeTruthy();
    expect(screen.getByRole("heading", { name: "4 profiles" })).toBeTruthy();
  });

  // Ticket 55: email apps are laid out as emails without a Profile, so a new one starts that way.
  it("starts a new Profile with the Email Layout the app gets today", async () => {
    await openProfiles({ profiles: [] });

    await click(screen.getAllByRole("button", { name: "Add profile" })[0] as HTMLElement);
    await click(appMenu().getByRole("menuitem", { name: /Outlook/ }));
    expect(screen.getByRole("switch", { name: "Email Layout" }).getAttribute("aria-checked")).toBe("true");

    await click(screen.getByRole("button", { name: "Change app" }));
    await click(appMenu().getByRole("menuitem", { name: /Microsoft Teams/ }));
    expect(screen.getByRole("switch", { name: "Email Layout" }).getAttribute("aria-checked")).toBe("false");
  });

  it("needs an app before it saves", async () => {
    await openProfiles();

    await click(screen.getByRole("button", { name: "Add profile" }));
    fireEvent.keyDown(appMenu().getAllByRole("menuitem")[0] as HTMLElement, { key: "Escape" });
    await settle();
    await click(screen.getByRole("button", { name: "Save profile" }));

    expect(screen.getByRole("alert").textContent).toBe("Choose the app this Profile is for.");
    // Esc closed the menu only: the editor is still open.
    expect(screen.getByRole("button", { name: "Save profile" })).toBeTruthy();
  });

  it("starts a new Profile's Formatting from what the app gets today, until the user picks one", async () => {
    await openProfiles({ profiles: [] });
    const formatting = () => within(screen.getByRole("radiogroup", { name: "Formatting" }));

    await click(screen.getAllByRole("button", { name: "Add profile" })[0] as HTMLElement);
    await click(appMenu().getByRole("menuitem", { name: /Visual Studio Code/ }));
    expect(formatting().getByRole("radio", { name: "Structured" }).getAttribute("aria-checked")).toBe("true");

    await click(formatting().getByRole("radio", { name: "Prose" }));
    await click(screen.getByRole("button", { name: "Change app" }));
    await click(appMenu().getByRole("menuitem", { name: /Word/ }));
    expect(formatting().getByRole("radio", { name: "Prose" }).getAttribute("aria-checked")).toBe("true");
  });

  it("edits a Profile in place, and its own app stays pickable", async () => {
    await openProfiles();

    await click(list().getByRole("button", { name: "Edit the Outlook profile" }));
    expect(screen.getByText("OUTLOOK.exe")).toBeTruthy();
    expect(screen.getByText("Word for word keeps every “um” and repeat. Default follows Settings (Standard).")).toBeTruthy();

    await click(screen.getByRole("button", { name: "Change app" }));
    expect((appMenu().getByRole("menuitem", { name: /Outlook/ }) as HTMLButtonElement).disabled).toBe(false);
    fireEvent.keyDown(appMenu().getAllByRole("menuitem")[0] as HTMLElement, { key: "Escape" });
    await settle();

    await click(within(screen.getByRole("radiogroup", { name: "Formatting" })).getByRole("radio", { name: "Structured" }));
    await click(screen.getByRole("button", { name: "Save profile" }));

    expect(list().getByText("Structured · Default cleanup · Email Layout")).toBeTruthy();
  });

  // Ticket 56: one app's own Style and "How I write here", in place of the old Tone box.
  it("gives one app its own Style and a note on how to write there", async () => {
    await openProfiles();

    await click(list().getByRole("button", { name: "Edit the Slack profile" }));
    expect(screen.getByRole("radio", { name: "Very casual" }).getAttribute("aria-checked")).toBe("true");
    expect(screen.getByText("Same as Work chat follows the Style screen.")).toBeTruthy();
    expect((screen.getByRole("textbox", { name: "How I write here" }) as HTMLInputElement).value).toBe("Bullet my status updates");

    await click(screen.getByRole("radio", { name: "Same as Work chat" }));
    await click(screen.getByRole("button", { name: "Save profile" }));

    expect(list().getByText("Prose · Light cleanup")).toBeTruthy();
  });

  it("keeps the words of an AI tool's Profile as said, unless it is given a Style", async () => {
    await openProfiles();

    await click(list().getByRole("button", { name: "Edit the Visual Studio Code profile" }));

    expect(screen.getByRole("radio", { name: "Same as AI tools and code" }).getAttribute("aria-checked")).toBe("true");
    expect(screen.getByText("Same as AI tools and code keeps your words and capitals as said.")).toBeTruthy();
  });

  it("puts a note that is too long under its field", async () => {
    await openProfiles();

    await click(list().getByRole("button", { name: "Edit the Outlook profile" }));
    fireEvent.change(screen.getByRole("textbox", { name: "How I write here" }), { target: { value: "x".repeat(201) } });
    await click(screen.getByRole("button", { name: "Save profile" }));

    expect(screen.getByRole("alert").textContent).toBe("Keep it to 200 characters or fewer.");
  });

  it("deletes a Profile, and Undo puts it back", async () => {
    await openProfiles();

    await click(list().getByRole("button", { name: "Edit the Slack profile" }));
    await click(screen.getByRole("button", { name: "Delete profile" }));

    expect(toast().textContent).toContain("Deleted the Slack profile");
    expect(screen.getByRole("heading", { name: "2 profiles" })).toBeTruthy();

    await click(within(toast()).getByRole("button", { name: "Undo" }));

    expect(list().getByText("Slack")).toBeTruthy();
    expect(screen.getByRole("heading", { name: "3 profiles" })).toBeTruthy();
  });

  it("offers no Delete while adding", async () => {
    await openProfiles();

    await click(screen.getByRole("button", { name: "Add profile" }));

    expect(screen.queryByRole("button", { name: "Delete profile" })).toBeNull();
  });
});
