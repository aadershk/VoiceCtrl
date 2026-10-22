import type { IconName } from "../icons";
import { Icon } from "./Icon";

export interface NavEntry<T extends string> {
  id: T;
  label: string;
  icon: IconName;
}

export interface NavListProps<T extends string> {
  items: readonly NavEntry<T>[];
  current: T;
  onSelect: (id: T) => void;
  label: string;
}

// 38 px items with a 4 px gap (§8): the highlight's offset for item i.
const ITEM_PITCH = 42;

/**
 * Nav items (§8) with one raised highlight that slides between them on the spring (§10 rule 6):
 * items never swap backgrounds.
 */
export function NavList<T extends string>({ items, current, onSelect, label }: NavListProps<T>) {
  const index = Math.max(0, items.findIndex((item) => item.id === current));
  return (
    <nav className="vc-nav" aria-label={label}>
      <div className="vc-nav-highlight" style={{ transform: `translateY(${index * ITEM_PITCH}px)` }} />
      {items.map((item) => (
        <button
          key={item.id}
          type="button"
          className="vc-nav-item"
          aria-current={item.id === current ? "page" : undefined}
          onClick={() => onSelect(item.id)}
        >
          <Icon name={item.icon} />
          {item.label}
        </button>
      ))}
    </nav>
  );
}
