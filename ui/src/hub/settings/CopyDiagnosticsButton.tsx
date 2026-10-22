import { useEffect, useRef, useState } from "react";
import { Button, TOAST_MS } from "../../design/components";
import { useHub } from "../hubContext";
import { GITHUB_ISSUES_URL, openLink } from "../links";

/**
 * Copy diagnostics (DESIGN §12): the host builds the scrubbed report and puts it on the clipboard.
 * The button turns into a check and "Copied" for 3.2 s, and the toast says what to do next.
 */
export function CopyDiagnosticsButton() {
  const { bridge, toast } = useHub();
  const [copied, setCopied] = useState(false);
  const [busy, setBusy] = useState(false);
  const reset = useRef<ReturnType<typeof setTimeout>>(undefined);

  useEffect(() => () => clearTimeout(reset.current), []);

  const copy = async () => {
    if (busy) return;
    setBusy(true);
    try {
      const { copied: ok } = await bridge.call("settings.copyDiagnostics", {});
      if (!ok) {
        toast("Couldn't copy. Another app is using the clipboard, so try again.", { error: true });
        return;
      }
      setCopied(true);
      clearTimeout(reset.current);
      reset.current = setTimeout(() => setCopied(false), TOAST_MS);
      toast("Diagnostics copied. Paste them into a GitHub issue.", {
        action: { label: "Open GitHub", onClick: () => openLink(GITHUB_ISSUES_URL) },
      });
    } catch {
      toast("Couldn't make the diagnostics. Try again.", { error: true });
    } finally {
      setBusy(false);
    }
  };

  return (
    <Button size="sm" icon={copied ? "check" : "copy"} onClick={() => void copy()}>
      {copied ? "Copied" : "Copy"}
    </Button>
  );
}
