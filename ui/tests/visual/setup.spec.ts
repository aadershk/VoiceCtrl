import { expect, test } from "@playwright/test";
import type { Page } from "@playwright/test";

// Every state of the approved C · Split board (research 13), in Light and Dark, reached by
// clicking through the page on the mock host. The screens the owner signs off become these baselines.

const KEY = "AIzaSyB7Qk2mL0pX4vT9sW1cE8rN6yH3f9a";

async function toKey(page: Page) {
  await page.getByRole("button", { name: "Continue" }).click();
  await page.getByText("Choose your Online provider").waitFor();
}

async function toModel(page: Page) {
  await page.getByRole("radio", { name: /Offline/ }).click();
  await page.getByRole("button", { name: "Continue" }).click();
  await page.getByText("Getting the offline model").waitFor();
}

async function submitKey(page: Page, key: string) {
  await page.getByLabel("Paste it here").fill(key);
  await page.getByRole("button", { name: "Continue" }).click();
}

const states: Record<string, { query: string; go: (page: Page) => Promise<void> }> = {
  mode: { query: "", go: async () => undefined },
  key: { query: "", go: toKey },
  "key-checking": {
    query: "checkDelay=600000",
    go: async (page) => {
      await toKey(page);
      await submitKey(page, KEY);
      await page.getByText("Checking the key").waitFor();
    },
  },
  "key-works": {
    query: "key=works&checkDelay=0",
    go: async (page) => {
      await page.clock.install();
      await toKey(page);
      await page.getByRole("button", { name: "Open AI Studio" }).click();
      await submitKey(page, KEY);
      await page.getByText("The key works").waitFor();
    },
  },
  "key-bad": {
    query: "key=didNotWork&checkDelay=0",
    go: async (page) => {
      await toKey(page);
      await submitKey(page, "AIzaSyB-3f9a");
      await page.getByRole("button", { name: "Use it anyway" }).waitFor();
    },
  },
  "key-no-internet": {
    query: "key=notChecked&checkDelay=0",
    go: async (page) => {
      await toKey(page);
      await submitKey(page, KEY);
      await page.getByRole("button", { name: "Use it anyway" }).waitFor();
    },
  },
  download: {
    query: "download=hold:312",
    go: async (page) => {
      await toModel(page);
      await page.getByText("312 of 700 MB").waitFor();
    },
  },
  "download-failed": {
    query: "download=fail:322",
    go: async (page) => {
      await toModel(page);
      await page.getByRole("button", { name: "Try again" }).waitFor();
    },
  },
  "download-done": {
    query: "download=done",
    go: async (page) => {
      await toModel(page);
      await page.getByText("Installed").waitFor();
    },
  },
  ready: {
    query: "key=works&checkDelay=0",
    go: async (page) => {
      await toKey(page);
      await submitKey(page, KEY);
      await page.getByText("You're all set").waitFor();
    },
  },
  // VED-70: changing a hold on the hotkey step. A refused combination shows its hint in the row
  // while listening; a saved Ctrl + Shift shows the language-switch warning.
  "ready-listening": {
    query: "key=works&checkDelay=0",
    go: async (page) => {
      await toKey(page);
      await submitKey(page, KEY);
      await page.getByText("You're all set").waitFor();
      await page.getByRole("button", { name: "Change talk keys" }).click();
      await page.keyboard.down("Meta");
      await page.keyboard.down("l");
      await page.keyboard.up("l");
      await page.keyboard.up("Meta");
      await page.getByText("Windows uses that combination itself. Choose another.").waitFor();
    },
  },
  "ready-warning": {
    query: "key=works&checkDelay=0",
    go: async (page) => {
      await toKey(page);
      await submitKey(page, KEY);
      await page.getByText("You're all set").waitFor();
      // Ctrl + Shift is the rewrite default, so move that first (Core refuses the same keys twice).
      await page.getByRole("button", { name: "Change rewrite keys" }).click();
      await page.keyboard.down("Control");
      await page.keyboard.down("Alt");
      await page.keyboard.up("Alt");
      await page.keyboard.up("Control");
      await page.getByText("Hold Left Ctrl + Left Alt to rewrite").waitFor();
      await page.getByRole("button", { name: "Change talk keys" }).click();
      await page.keyboard.down("Control");
      await page.keyboard.down("Shift");
      await page.keyboard.up("Shift");
      await page.keyboard.up("Control");
      await page.getByText(/Windows may switch the input language/).waitFor();
    },
  },
  // VED-74: several keyboard layouts are installed, so the Rewrite default (Left Ctrl + Left Shift)
  // says it also switches the language. A notice in the row, not a blocker.
  "ready-language-conflict": {
    query: "key=works&checkDelay=0&languages=many",
    go: async (page) => {
      await toKey(page);
      await submitKey(page, KEY);
      await page.getByText("You're all set").waitFor();
      await page.getByText(/also switches your keyboard language/).waitFor();
    },
  },
  "ready-key-skipped": {
    query: "",
    go: async (page) => {
      await toKey(page);
      await page.getByRole("button", { name: "Skip for now" }).click();
      await page.getByText("You're all set").waitFor();
    },
  },
  "ready-model-skipped": {
    query: "download=hold:100",
    go: async (page) => {
      await toModel(page);
      await page.getByRole("button", { name: "Skip for now" }).click();
      await page.getByText("You're all set").waitFor();
    },
  },
};

test.use({ viewport: { width: 900, height: 620 } });

for (const theme of ["light", "dark"] as const) {
  for (const [name, state] of Object.entries(states)) {
    test(`setup ${name}, ${theme}`, async ({ page }) => {
      // AI Studio would open a browser tab; the host does that in the app.
      await page.addInitScript(() => {
        window.open = () => null;
      });
      await page.goto(`/setup.html?theme=${theme}&${state.query}`);
      await page.evaluate(() => document.fonts.ready);
      await expect(page.locator("html")).not.toHaveAttribute("data-first-paint");
      await state.go(page);
      await expect(page).toHaveScreenshot(`setup-${name}-${theme}.png`);
    });
  }
}
