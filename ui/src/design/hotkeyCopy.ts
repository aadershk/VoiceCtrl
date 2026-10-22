/** The default bindings' words, shared by the status card, the tray flyout, Dictations and Setup. */

/** "Hold Left Ctrl + Left Win to talk". */
export function talkSentence(keys: readonly string[]): string {
  return `Hold ${keys.join(" + ")} to talk`;
}

/**
 * The Ready hint under the status card and in the flyout: the hold-to-talk keys when the Trigger
 * Key is on, else the Hotkey's double-tap (the bar must be shown before it can be clicked).
 */
export function readyHint(hotkey: string, talkKeys?: readonly string[] | null): string {
  return talkKeys && talkKeys.length > 0 ? talkSentence(talkKeys) : `Double-tap ${hotkey} to show the bar`;
}
