import { expect, test } from "@playwright/test";

// Every state of the approved Mini board (research 11, DESIGN §9) and every transient notice, in
// Light and Dark, on the mock host. The page is shot on a desk-coloured ground, since in the app
// it is transparent over whatever is on screen.

const states: Record<string, string> = {
  ready: "state=ready",
  listening: "state=listening&level=.55",
  "listening-hover": "state=listening&level=.55&hover=1",
  // Hands-free lock (VED-74): the Listening look plus a padlock.
  "listening-locked": "state=listening&level=.55&locked=1",
  "listening-locked-hover": "state=listening&level=.55&hover=1&locked=1",
  transcribing: "state=transcribing",
  "transcribing-hover": "state=transcribing&hover=1",
  // An Email request (ticket 57): after a Rewrite hold, Transcribing says it is writing the email.
  "writing-email": "state=transcribing&rewrite=1&writing=1",
  "writing-email-hover": "state=transcribing&hover=1&rewrite=1&writing=1",
  "still-writing": "state=notice&notice=stillWriting",
  pasted: "state=pasted",
  "no-speech": "state=noSpeech",
  "failed-no-internet": "state=failed&reason=noInternet",
  "failed-service-error": "state=failed&reason=serviceError",
  "mic-unavailable": "state=notice&notice=micUnavailable",
  "add-api-key": "state=notice&notice=addApiKey",
  "copied-press-ctrl-v": "state=notice&notice=copiedPressCtrlV",
  "paste-failed": "state=notice&notice=pasteFailed",
  "copy-failed": "state=notice&notice=copyFailed",
  // Rewrite by voice (ticket 39): the same pill, set apart from a Dictation, and its three notices.
  "rewrite-listening": "state=listening&level=.55&rewrite=1",
  "rewrite-listening-hover": "state=listening&level=.55&hover=1&rewrite=1",
  "rewrite-transcribing": "state=transcribing&rewrite=1",
  "nothing-to-rewrite": "state=notice&notice=nothingToRewrite",
  "rewrite-needs-model": "state=notice&notice=rewriteNeedsLanguageModel",
  "rewrite-failed": "state=notice&notice=rewriteFailed",
  "email-not-written": "state=notice&notice=emailNotWritten",
  // Pill actions and notices (VED-75): Finish beside the X, a press while Transcribing, and the
  // clearer failures.
  "paste-failed-key": "state=notice&notice=pasteFailed&key=Ctrl%2BShift%2BV",
  "select-textbox-first": "state=notice&notice=selectTextboxFirst",
  "no-mic-audio": "state=notice&notice=noMicAudio",
  "no-words-heard": "state=notice&notice=noWordsHeard",
  "mic-stopped": "state=notice&notice=micStopped",
  "still-transcribing": "state=notice&notice=stillTranscribing",
  "taking-longer": "state=notice&notice=takingLonger",
  // Cancel safety and the recording cap (VED-76): the Undo notice, and the two cap notices.
  "cancelled-undo": "state=notice&notice=cancelledUndo",
  "recording-limit-soon": "state=notice&notice=recordingLimitSoon",
  "recording-limit-reached": "state=notice&notice=recordingLimitReached",
  "right-still-transcribing": "state=notice&notice=stillTranscribing&dock=right",
  // Always show: the grey handle (tinted for the desktop, which follows the Theme here) and the
  // Ready tile it turns into under the pointer.
  handle: "state=handle",
  "handle-hover": "state=handle&hover=1",
  "handle-left": "state=handle&dock=left",
  // The side docks grow away from the screen edge.
  "left-listening-hover": "state=listening&level=.55&hover=1&dock=left",
  "right-listening-hover": "state=listening&level=.55&hover=1&dock=right",
  "right-failed-service-error": "state=failed&reason=serviceError&dock=right",
};

const ground = { light: "#E4DDD3", dark: "#1C1E24" };

test.use({ viewport: { width: 360, height: 120 } });

for (const theme of ["light", "dark"] as const) {
  for (const [name, query] of Object.entries(states)) {
    test(`overlay ${name}, ${theme}`, async ({ page }) => {
      // The hover timer reads the clock; a fixed one keeps it at 0:07.
      await page.clock.setFixedTime(new Date("2026-09-29T12:00:00Z"));
      await page.goto(`/overlay.html?theme=${theme}&${query}`);
      await page.addStyleTag({ content: `body { background: ${ground[theme]} !important; }` });
      await page.evaluate(() => document.fonts.ready);
      await expect(page.locator("html")).not.toHaveAttribute("data-first-paint");
      await expect(page.locator("#stage")).toHaveScreenshot(`overlay-${name}-${theme}.png`);
    });
  }
}

test("the page is transparent around the pill", async ({ page }) => {
  await page.goto("/overlay.html?state=ready");
  const background = await page.evaluate(() => getComputedStyle(document.body).backgroundColor);
  expect(background).toBe("rgba(0, 0, 0, 0)");
});
