import { Fragment, useEffect, useRef, useState } from "react";
import { BridgeError } from "../../bridge/bridge";
import type { DictationItem, FailedDictationItem, HubPasteResult } from "../../bridge/contracts.g";
import {
  ArtTile,
  Button,
  EmptyState,
  FailedNotice,
  IconButton,
  Keys,
  LatestHero,
  ListRow,
  Menu,
  MenuAnchor,
  MenuItem,
  SearchField,
} from "../../design/components";
import { useFlipList } from "../../design/motion";
import { dayGroups, failedDetail, failedTitle, latestContext, matches, rowMeta, subtitleFor } from "../dictations/format";
import { useDictations, useNow } from "../dictations/useDictations";
import { useHub } from "../hubContext";
import type { HubApi } from "../hubContext";
import { screenById } from "../screens";
import { ScreenHeader } from "./ScreenHeader";

/** How long Copy reads "Copied" (research 10). */
const COPIED_MS = 1200;

/** Says how a Paste Again from the Hub went; the paste itself happens in the other app. */
function pasteToast(hub: HubApi, result: HubPasteResult, keys: readonly string[]) {
  const app = result.app;
  switch (result.outcome) {
    case "pasted":
      return hub.toast(app ? `Pasted into ${app}` : "Pasted into the window you used last");
    case "copied":
      return hub.toast(`Copied. Press Ctrl+V in ${app ?? "that app"} to paste it.`);
    case "failed":
      return hub.toast(`Couldn't paste into ${app ?? "that app"}. Use Copy instead.`, { error: true });
    case "noWindow":
      return hub.toast("Copied. Click where it should go and press Ctrl+V.");
    case "busy":
      return hub.toast(`Paste Again waits until the Dictation in progress is done. Then press ${keys.join(" ")}.`);
  }
}

/**
 * Dictations (research 10, DESIGN §8): the Latest hero, a notice per Failed Dictation, and the
 * kept Dictations in day groups with Search, row actions and the ··· menu. Always the Hub's first
 * screen. What is kept follows Keep Dictations; audio never is.
 */
export function DictationsScreen() {
  const hub = useHub();
  const { bridge, app, toast, confirm, go } = hub;
  const view = useDictations(bridge);
  const now = useNow();
  const [query, setQuery] = useState("");
  const [menuOpen, setMenuOpen] = useState(false);
  const [copied, setCopied] = useState<string | null>(null);
  const listRef = useRef<HTMLDivElement>(null);
  const { label } = screenById("dictations");

  useEffect(() => {
    if (copied === null) return;
    const timer = setTimeout(() => setCopied(null), COPIED_MS);
    return () => clearTimeout(timer);
  }, [copied]);

  const keepNone = view?.keep === "none";
  const all = keepNone ? [] : (view?.dictations ?? []);
  const searching = query.trim().length > 0;
  const [latest, ...older] = all;
  const listed = searching ? all.filter((d) => matches(d, query)) : older;
  const groups = dayGroups(listed, now);
  useFlipList(listRef, [listed.map((d) => d.id).join()]);

  const header = (
    <ScreenHeader
      title={label}
      subtitle={subtitleFor(view?.keep ?? "last50")}
      actions={
        all.length > 0 && (
          <>
            <SearchField
              label="Search Dictations"
              className="dict-search"
              value={query}
              onChange={(event) => setQuery(event.target.value)}
            />
            <MenuAnchor>
              <IconButton icon="more" label="More" className="dict-more" onClick={() => setMenuOpen((open) => !open)} />
              <Menu open={menuOpen} onClose={() => setMenuOpen(false)} label="Dictations" placement="below-end">
                <MenuItem icon="copy" onSelect={() => void copyAll()}>
                  Copy all as text
                </MenuItem>
                <MenuItem icon="delete" danger onSelect={() => void clearAll()}>
                  Clear all Dictations
                </MenuItem>
              </Menu>
            </MenuAnchor>
          </>
        )
      }
    />
  );

  // Nothing that depends on the host renders before it answers (spec §4).
  if (!view) return <div className="hub-screen">{header}</div>;

  async function copy(item: DictationItem, marker: string) {
    try {
      await bridge.call("dictations.copy", { id: item.id });
      setCopied(marker);
    } catch (error) {
      copyFailed(error);
    }
  }

  function copyFailed(error: unknown) {
    if (error instanceof BridgeError && error.code !== "copy_failed") return;
    toast("Couldn't copy. Another app is using the clipboard, so try again.", { error: true });
  }

  async function copyAll() {
    try {
      const { count } = await bridge.call("dictations.copyAll", {});
      toast(`Copied ${count} ${count === 1 ? "Dictation" : "Dictations"} as text`);
    } catch (error) {
      copyFailed(error);
    }
  }

  async function clearAll() {
    const count = all.length;
    const ok = await confirm({
      title: "Clear all Dictations?",
      body: `This removes all ${count} ${count === 1 ? "Dictation" : "Dictations"} from this PC. It can't be undone. Your Dictionary, Snippets and Profiles stay.`,
      confirmLabel: "Clear all",
    });
    if (!ok) return;
    await bridge.call("dictations.clearAll", {});
    setQuery("");
    toast("All Dictations cleared");
  }

  async function pasteAgain(item: DictationItem | null) {
    const result = await bridge.call("dictations.pasteAgain", { id: item?.id ?? null });
    pasteToast(hub, result, view?.pasteAgainKeys ?? []);
  }

  async function restoreOriginal(item: DictationItem) {
    await bridge.call("dictations.restoreOriginal", { id: item.id });
    toast("Original restored. This is what you said, before Smart cleanup.");
  }

  async function remove(item: DictationItem) {
    await bridge.call("dictations.delete", { id: item.id });
    toast("Dictation deleted", {
      action: { label: "Undo", onClick: () => void bridge.call("dictations.undoDelete", { id: item.id }) },
    });
  }

  async function retry(failed: FailedDictationItem, offline: boolean) {
    const { outcome } = await bridge.call("dictations.retry", { id: failed.id, offline });
    const done = offline ? "Transcribed offline." : "Transcribed.";
    if (outcome === "transcribed") {
      toast(keepNone ? `${done} Press ${view?.pasteAgainKeys.join(" ")} to paste it.` : `${done} It's at the top, ready to paste.`);
    } else if (outcome === "noSpeech") {
      toast("There was no speech in that recording, so it was dropped.");
    } else if (outcome === "failed") {
      toast("It didn't go through again. The audio is still kept.", { error: true });
    }
  }

  async function dismiss(failed: FailedDictationItem) {
    await bridge.call("dictations.dismiss", { id: failed.id });
    toast("Dismissed. The audio is gone.");
  }

  const hotkey = app.status.hotkey;
  const talkKeys = app.status.talkKeys;
  return (
    <div className="hub-screen">
      {header}
      {!searching && latest && (
        <LatestHero
          text={latest.text}
          context={latestContext(latest, now)}
          pasteKeys={view.pasteAgainKeys}
          copied={copied === "latest"}
          onPasteAgain={() => void pasteAgain(null)}
          onCopy={() => void copy(latest, "latest")}
          onRestoreOriginal={latest.hasOriginal ? () => void restoreOriginal(latest) : undefined}
        />
      )}
      {!searching && view.failed.length > 0 && (
        <div className="dict-notices">
          {view.failed.map((failed) => (
            <FailedNotice
              key={failed.id}
              title={failedTitle(failed)}
              detail={failedDetail(failed)}
              busy={failed.retrying}
              retryLabel={failed.reason === "cancelled" ? "Recover text" : "Retry"}
              retryOfflineLabel={failed.reason === "cancelled" ? "Recover offline" : "Retry offline"}
              onRetryOffline={view.canRetryOffline ? () => void retry(failed, true) : undefined}
              onRetry={() => void retry(failed, false)}
              onDismiss={() => void dismiss(failed)}
            />
          ))}
        </div>
      )}
      {groups.length > 0 && (
        <div ref={listRef} className="dict-groups">
          {groups.map((group) => (
            <section key={group.label} className="hub-group" aria-label={group.label}>
              <h2 className="vc-group">{group.label}</h2>
              {group.items.map((item, i) => (
                <Fragment key={item.id}>
                  {i > 0 && <div className="vc-sep" />}
                  <ListRow
                    flipKey={item.id}
                    primary={item.text}
                    meta={rowMeta(item)}
                    actions={
                      <>
                        <IconButton
                          icon={copied === item.id ? "check" : "copy"}
                          label={copied === item.id ? "Copied" : "Copy"}
                          onClick={() => void copy(item, item.id)}
                        />
                        {item.hasOriginal && (
                          <IconButton icon="retry" label="Restore original" onClick={() => void restoreOriginal(item)} />
                        )}
                        <IconButton icon="paste" label="Paste Again" onClick={() => void pasteAgain(item)} />
                        <IconButton icon="delete" label="Delete" onClick={() => void remove(item)} />
                      </>
                    }
                  />
                </Fragment>
              ))}
            </section>
          ))}
        </div>
      )}
      {searching && listed.length === 0 && (
        <EmptyState
          art={<ArtTile art="search" />}
          title={`No Dictations match “${query.trim()}”`}
          actions={<Button onClick={() => setQuery("")}>Clear search</Button>}
        >
          Search looks through the text of the Dictations kept on this PC.
        </EmptyState>
      )}
      {keepNone && (
        <EmptyState
          art={<ArtTile art="dictations" />}
          title="Dictations aren't kept"
          actions={<Button onClick={() => go("settings", "keep-dictations")}>Change in Settings</Button>}
        >
          You chose not to keep them, so nothing is stored on this PC. Paste Again still works for your latest
          Transcription until VoiceCtrl quits.
        </EmptyState>
      )}
      {!keepNone && all.length === 0 && (
        <EmptyState
          art={<ArtTile art="logo" />}
          title="Your Dictations will show up here"
          actions={<Keys keys={talkKeys && talkKeys.length > 0 ? talkKeys : [hotkey, hotkey]} />}
        >
          {talkKeys && talkKeys.length > 0 ? (
            <>
              Hold <b>{talkKeys.join(" + ")}</b> anywhere and speak, then let go. VoiceCtrl types it where your cursor is.
              Or double-tap <b>{hotkey}</b> to bring up the bar and click it.
            </>
          ) : (
            <>
              Double-tap <b>{hotkey}</b> anywhere to bring up the bar, click it and speak, then double-tap <b>{hotkey}</b> again
              to paste. VoiceCtrl types it where your cursor is.
            </>
          )}
        </EmptyState>
      )}
    </div>
  );
}
