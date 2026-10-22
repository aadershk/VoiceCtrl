import type { UpdateView } from "../../bridge/contracts.g";
import { Button, ProgressBar, Spinner, Switch } from "../../design/components";

export interface UpdatesControlProps {
  view: UpdateView;
  onCheck: () => void;
  onDownload: () => void;
  onRestart: () => void;
  onAutomatic: (on: boolean) => void;
}

/**
 * Automatic updates (DESIGN §12): the state's control, then the switch. The state line itself is
 * the row's description (`updateLine`).
 */
export function UpdatesControl({ view, onCheck, onDownload, onRestart, onAutomatic }: UpdatesControlProps) {
  return (
    <div className="hub-update">
      {stateControl(view, { onCheck, onDownload, onRestart })}
      <Switch label="Automatic updates" checked={view.automatic} onChange={onAutomatic} />
    </div>
  );
}

function stateControl(view: UpdateView, actions: Pick<UpdatesControlProps, "onCheck" | "onDownload" | "onRestart">) {
  switch (view.state) {
    case "checking":
      return (
        <span className="hub-update-busy">
          <Spinner />
          Checking
        </span>
      );
    case "downloading": {
      const percent = Math.floor(view.progress * 100);
      return (
        <span className="hub-update-busy">
          <span className="hub-update-percent">{percent}%</span>
          <ProgressBar value={view.progress} label={`Downloading ${view.newVersion ?? "the update"}`} width={140} />
        </span>
      );
    }
    case "ready":
      return (
        <Button size="sm" onClick={actions.onRestart}>
          Restart now
        </Button>
      );
    case "available":
      return (
        <Button size="sm" icon="download" onClick={actions.onDownload}>
          Download and install
        </Button>
      );
    default:
      return (
        <Button size="sm" onClick={actions.onCheck}>
          Check now
        </Button>
      );
  }
}
