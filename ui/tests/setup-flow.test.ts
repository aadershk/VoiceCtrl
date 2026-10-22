import { describe, expect, it } from "vitest";
import type { ModelDownloadStatus, SetupSnapshot } from "../src/bridge/contracts.g";
import { initialState, keyPrimary, railIndex, setupReducer, skipNote } from "../src/setup/flow";
import type { SetupAction, SetupState } from "../src/setup/flow";
import { downloadLine, downloadPercent, timeLeft } from "../src/setup/format";

const MiB = 1024 * 1024;
const idle: ModelDownloadStatus = { state: "idle", bytes: 0, totalBytes: 700 * MiB, bytesPerSecond: 0 };
const snapshot: SetupSnapshot = { hotkey: "Ctrl", startWithWindows: true, model: idle };

const run = (actions: SetupAction[], from: SetupState = initialState(snapshot)) => actions.reduce(setupReducer, from);

describe("Setup flow", () => {
  it("starts on step 1 with Online picked and Start with Windows on", () => {
    const state = initialState(snapshot);
    expect(state).toMatchObject({ step: "mode", listening: "online", startWithWindows: true });
    expect(railIndex(state.step)).toBe(1);
  });

  it("goes to the key for Online and to the model for Offline", () => {
    expect(run([{ type: "continueFromMode" }]).step).toBe("key");
    expect(run([{ type: "pick", listening: "offline" }, { type: "continueFromMode" }]).step).toBe("model");
  });

  it("keeps Continue off while the key is empty, checking, or has just worked", () => {
    const key = run([{ type: "continueFromMode" }]);
    expect(keyPrimary(key)).toEqual({ label: "Continue", disabled: true });

    const typed = run([{ type: "editKey", key: "AIza123" }], key);
    expect(keyPrimary(typed).disabled).toBe(false);
    expect(keyPrimary(run([{ type: "checkingKey" }], typed)).disabled).toBe(true);
    expect(keyPrimary(run([{ type: "checkingKey" }, { type: "keyChecked", key: "AIza123", check: "works" }], typed)).disabled).toBe(true);
  });

  it("offers Use it anyway when the key didn't work or couldn't be checked, and editing clears it", () => {
    const typed = run([{ type: "continueFromMode" }, { type: "editKey", key: "AIzabad" }, { type: "checkingKey" }]);
    for (const check of ["didNotWork", "notChecked"] as const) {
      const checked = run([{ type: "keyChecked", key: "AIzabad", check }], typed);
      expect(keyPrimary(checked)).toEqual({ label: "Use it anyway", disabled: false });
      expect(run([{ type: "useKey" }], checked)).toMatchObject({ step: "ready", usedKey: "AIzabad", skipped: null });
      expect(run([{ type: "editKey", key: "AIzabad2" }], checked).keyState).toBe("idle");
    }
  });

  it("ignores a key check that comes back after the key changed or the user went Back", () => {
    const checking = run([{ type: "continueFromMode" }, { type: "editKey", key: "one" }, { type: "checkingKey" }]);
    const edited = run([{ type: "editKey", key: "two" }, { type: "keyChecked", key: "one", check: "works" }], checking);
    expect(edited.keyState).toBe("idle");

    const back = run([{ type: "back" }, { type: "keyChecked", key: "one", check: "works" }], checking);
    expect(back).toMatchObject({ step: "mode", keyState: "idle" });
  });

  it("does not move on by itself unless the key worked", () => {
    const typed = run([{ type: "continueFromMode" }, { type: "editKey", key: "AIza" }]);
    expect(run([{ type: "useKey" }], typed).step).toBe("key");
  });

  it("skipping the key reaches the last step with the key note and no key", () => {
    const state = run([{ type: "continueFromMode" }, { type: "editKey", key: "AIza" }, { type: "skip" }]);
    expect(state).toMatchObject({ step: "ready", skipped: "key", usedKey: null });
    expect(skipNote(state)).toMatch(/^No key yet/);
  });

  it("skipping the model notes it, unless the model was already done", () => {
    const toModel: SetupAction[] = [{ type: "pick", listening: "offline" }, { type: "continueFromMode" }];
    expect(skipNote(run([...toModel, { type: "skip" }]))).toBe("The offline model downloads the first time you dictate.");

    const done = { ...idle, state: "done" as const, bytes: idle.totalBytes };
    expect(run([...toModel, { type: "model", status: done }, { type: "skip" }]).skipped).toBeNull();
  });

  it("enables Continue on the model step only once it is done", () => {
    const model = run([{ type: "pick", listening: "offline" }, { type: "continueFromMode" }]);
    expect(run([{ type: "continueFromModel" }], model).step).toBe("model");

    const done = run([{ type: "model", status: { ...idle, state: "done", bytes: idle.totalBytes } }, { type: "continueFromModel" }], model);
    expect(done).toMatchObject({ step: "ready", skipped: null });
  });

  it("Back from the last step returns to the step 2 that was chosen, and a worked key is checked again", () => {
    const ready = run([
      { type: "continueFromMode" },
      { type: "editKey", key: "AIza-good-key-000000" },
      { type: "checkingKey" },
      { type: "keyChecked", key: "AIza-good-key-000000", check: "works" },
      { type: "useKey" },
    ]);
    expect(run([{ type: "back" }], ready)).toMatchObject({ step: "key", keyState: "idle", key: "AIza-good-key-000000" });

    const offlineReady = run([{ type: "pick", listening: "offline" }, { type: "continueFromMode" }, { type: "skip" }]);
    expect(run([{ type: "back" }], offlineReady).step).toBe("model");
  });

  it("can't change the choice once past step 1", () => {
    const key = run([{ type: "continueFromMode" }, { type: "pick", listening: "offline" }]);
    expect(key.listening).toBe("online");
  });
});

describe("Setup download text", () => {
  const at = (mb: number, state: ModelDownloadStatus["state"], rate = 10 * MiB): ModelDownloadStatus => ({
    state,
    bytes: mb * MiB,
    totalBytes: 700 * MiB,
    bytesPerSecond: rate,
  });

  it("writes the board's lines", () => {
    expect(downloadLine(at(312, "downloading"))).toBe("312 of 700 MB · about 40 s left");
    expect(downloadLine(at(322, "failed"))).toBe("322 of 700 MB · stopped");
    expect(downloadLine(at(700, "done"))).toBe("Installed · 700 MB · ready to use");
  });

  it("leaves the time out until there is a rate", () => {
    expect(downloadLine(at(0, "downloading", 0))).toBe("0 of 700 MB");
  });

  it("rounds the time left", () => {
    expect(timeLeft(400 * MiB, 2 * MiB)).toBe("about 3 min left");
    expect(timeLeft(3 * MiB, 10 * MiB)).toBe("about 5 s left");
    expect(timeLeft(0, 10 * MiB)).toBeNull();
  });

  it("holds the percentage at 99 until the host says done", () => {
    expect(downloadPercent(at(700, "downloading"))).toBe(99);
    expect(downloadPercent(at(750, "downloading"))).toBe(99);
    expect(downloadPercent(at(700, "done"))).toBe(100);
    expect(downloadPercent(at(322, "failed"))).toBe(46);
  });
});

describe("Setup flow, Smart cleanup answer", () => {
  it("defaults to downloading the model and follows the switch", () => {
    expect(initialState(snapshot).smartCleanup).toBe(true);
    expect(run([{ type: "smartCleanup", on: false }]).smartCleanup).toBe(false);
  });
});
