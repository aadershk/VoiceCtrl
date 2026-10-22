import { describe, expect, it } from "vitest";
import type { DictationItem, FailedDictationItem } from "../src/bridge/contracts.g";
import {
  dayGroups,
  failedDetail,
  failedTitle,
  latestContext,
  matches,
  rowMeta,
  subtitleFor,
} from "../src/hub/dictations/format";

// Tuesday 29 September 2026, 10:00 local time.
const NOW = new Date(2026, 8, 29, 10, 0).getTime();
const at = (day: number, hour: number, minute = 0) => new Date(2026, 8, day, hour, minute).getTime();
const item = (id: string, atMs: number, text = id, app: string | null = null): DictationItem => ({ id, text, atMs, app, hasOriginal: false });
const LOCALE = "en-GB";

describe("dayGroups", () => {
  it("groups newest first into Today, Yesterday, weekdays of this week, then dates", () => {
    const groups = dayGroups(
      [item("a", at(29, 9)), item("b", at(29, 8)), item("c", at(28, 18)), item("d", at(26, 12)), item("e", at(14, 9))],
      NOW,
      LOCALE,
    );

    expect(groups.map((g) => [g.label, g.items.map((i) => i.id)])).toEqual([
      ["Today", ["a", "b"]],
      ["Yesterday", ["c"]],
      ["Saturday", ["d"]],
      ["14 September", ["e"]],
    ]);
  });
});

describe("latestContext", () => {
  it("names the app and how long ago", () => {
    expect(latestContext(item("a", NOW - 2 * 60_000, "x", "Outlook"), NOW, LOCALE)).toBe("into Outlook · 2 min ago");
    expect(latestContext(item("a", NOW - 20_000), NOW, LOCALE)).toBe("just now");
    expect(latestContext(item("a", at(29, 7, 5)), NOW, LOCALE)).toBe("today at 07:05");
    expect(latestContext(item("a", at(28, 18, 5), "x", "Slack"), NOW, LOCALE)).toBe("into Slack · yesterday at 18:05");
  });
});

describe("rows", () => {
  it("show the time, then the app when it is known", () => {
    expect(rowMeta(item("a", at(29, 9, 12), "x", "Notion"), LOCALE)).toBe("09:12 · Notion");
    expect(rowMeta(item("a", at(29, 9, 12)), LOCALE)).toBe("09:12");
  });

  it("match a search anywhere in the text, ignoring case and spacing", () => {
    expect(matches(item("a", NOW, "Send the Invoice today"), "  invoice ")).toBe(true);
    expect(matches(item("a", NOW, "Send the Invoice today"), "receipt")).toBe(false);
  });
});

describe("Failed Dictation notices", () => {
  const failed: FailedDictationItem = { id: "f", reason: "noInternet", atMs: at(29, 9, 41), app: "Slack", audioSeconds: 12, retrying: false };

  it("say why, then where, when and how much audio is kept", () => {
    expect(failedTitle(failed)).toBe("One Dictation didn't go through: no internet");
    expect(failedTitle({ ...failed, reason: "timedOut" })).toBe("One Dictation didn't go through: it timed out");
    expect(failedDetail(failed, LOCALE)).toBe("Slack · 09:41 · 12 s of audio, kept until you retry, dismiss it or quit");
    expect(failedDetail({ ...failed, app: null }, LOCALE)).toBe("09:41 · 12 s of audio, kept until you retry, dismiss it or quit");
  });

  it("tell a cancelled recording apart: recoverable, and kept for 10 minutes", () => {
    const cancelled = { ...failed, reason: "cancelled" as const };

    expect(failedTitle(cancelled)).toBe("You cancelled a recording: its text can still be recovered");
    expect(failedDetail(cancelled, LOCALE)).toBe(
      "Slack · 09:41 · 12 s of audio, kept for 10 minutes, until you recover it, dismiss it or quit",
    );
  });
});

describe("subtitleFor", () => {
  it("follows Keep Dictations", () => {
    expect(subtitleFor("last50")).toBe("Your last 50, kept on this PC. Audio is never saved.");
    expect(subtitleFor("last24Hours")).toBe("The last 24 hours, kept on this PC. Audio is never saved.");
    expect(subtitleFor("none")).toBe("Not kept. Audio is never saved either.");
  });
});
