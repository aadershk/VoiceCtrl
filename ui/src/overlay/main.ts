import "../design/tokens.css";
import "../design/base.css";
import "./overlay.css";
import { createBridge, webViewTransport } from "../bridge/bridge";
import type { OverlayState } from "../bridge/contracts.g";
import { installOverlayMock } from "./mockOverlay";
import { createOverlay } from "./overlay";

const stage = document.getElementById("stage");
if (!stage) throw new Error("overlay.html has no #stage");

// In the app, WebView2's transport; in a plain browser, the mock host (see mockOverlay.ts).
const transport = webViewTransport();
const mock = transport ? null : installOverlayMock(location.search);
const bridge = createBridge(transport ?? mock!.host.transport);

// Commands are fire-and-forget here: a failure is logged by the host, and the page has nothing
// to show for it.
const send = (promise: Promise<unknown>) => void promise.catch(() => undefined);

const root = document.documentElement;
const app = await bridge.ready();
root.dataset.theme = app.theme;
if (transport) root.dataset.reducedMotion = String(app.reducedMotion);
bridge.on("app.theme", ({ theme }) => {
  root.dataset.theme = theme;
});
bridge.on("app.reducedMotion", ({ reducedMotion }) => {
  root.dataset.reducedMotion = String(reducedMotion);
});

const overlay = createOverlay(stage, {
  act(action) {
    switch (action.kind) {
      case "start":
        send(bridge.call("overlay.start", {}));
        break;
      case "cancel":
        send(bridge.call("overlay.cancel", {}));
        break;
      case "finish":
        send(bridge.call("overlay.finish", {}));
        break;
      case "press":
        send(bridge.call("overlay.press", {}));
        break;
      case "openKeySettings":
        send(bridge.call("overlay.openKeySettings", {}));
        break;
      case "retry":
        send(bridge.call("overlay.retry", { failedId: action.failedId }));
        break;
      case "dismiss":
        send(bridge.call("overlay.dismiss", { failedId: action.failedId }));
        break;
      case "undo":
        send(bridge.call("overlay.undo", { failedId: action.failedId }));
        break;
    }
  },
  reportHitRect: (rect) => send(bridge.call("overlay.hitRect", rect)),
  painted: (seq) => send(bridge.call("overlay.painted", { seq })),
});

// Views are numbered; the answer to overlay.state can arrive after a newer event.
let latest = -1;
const apply = (state: OverlayState) => {
  if (state.seq < latest) return;
  latest = state.seq;
  overlay.render(state);
};
bridge.on("overlay.view", ({ state }) => apply(state));
bridge.on("overlay.level", ({ level }) => overlay.level(level));
bridge.on("overlay.pointer", ({ kind, x, y }) => overlay.pointer(kind, x, y));
bridge.on("overlay.layoutChanged", ({ layout }) => overlay.layout(layout));
bridge.on("overlay.drag", ({ dragging }) => overlay.drag(dragging));
overlay.layout(await bridge.call("overlay.layout", {}));
apply(await bridge.call("overlay.state", {}));
mock?.attach(overlay, stage);

// Nothing animates on first paint (§10 rule 7): a reload mid-Dictation shows the pill at once.
requestAnimationFrame(() => requestAnimationFrame(() => delete root.dataset.firstPaint));
