import { useEffect, useRef, useState } from 'react';
import { Button } from '../../components/Button';
import { COLUMN_IDS, REQUIRED_COLUMNS, type ColumnId, type SavedView } from './gridPreferences';

/** Closes a popover on outside click or Escape. */
function useDismiss(open: boolean, onDismiss: () => void) {
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) {
      return;
    }

    function onPointerDown(event: MouseEvent) {
      if (ref.current && !ref.current.contains(event.target as Node)) {
        onDismiss();
      }
    }
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        onDismiss();
      }
    }

    document.addEventListener('mousedown', onPointerDown);
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('mousedown', onPointerDown);
      document.removeEventListener('keydown', onKeyDown);
    };
  }, [open, onDismiss]);

  return ref;
}

const COLUMN_LABELS: Record<ColumnId, string> = {
  product: 'Product',
  category: 'Category',
  status: 'Status',
  completeness: 'Completeness',
  channels: 'Channels',
  languages: 'Languages',
  variants: 'Variants',
  owner: 'Owner',
  updated: 'Updated',
};

export interface ColumnChooserProps {
  visible: ColumnId[];
  onChange: (columns: ColumnId[]) => void;
}

export function ColumnChooser({ visible, onChange }: ColumnChooserProps) {
  const [open, setOpen] = useState(false);
  const ref = useDismiss(open, () => setOpen(false));

  function toggle(id: ColumnId, checked: boolean) {
    onChange(
      checked
        ? COLUMN_IDS.filter((columnId) => columnId === id || visible.includes(columnId))
        : visible.filter((columnId) => columnId !== id),
    );
  }

  return (
    <div className="pop" ref={ref}>
      <Button aria-expanded={open} aria-haspopup="true" onClick={() => setOpen((o) => !o)}>
        Columns
      </Button>
      {open && (
        <div className="pop-panel" role="group" aria-label="Choose columns">
          <div className="pop-cap">Visible columns</div>
          {COLUMN_IDS.map((id) => {
            const required = REQUIRED_COLUMNS.includes(id);
            return (
              <label key={id} className="pop-row">
                <input
                  type="checkbox"
                  checked={visible.includes(id)}
                  disabled={required}
                  onChange={(event) => toggle(id, event.target.checked)}
                />
                {COLUMN_LABELS[id]}
                {required && <span className="x">always</span>}
              </label>
            );
          })}
        </div>
      )}
    </div>
  );
}

export interface SavedViewsMenuProps {
  views: SavedView[];
  onApply: (view: SavedView) => void;
  onSave: (name: string) => void;
  onDelete: (id: string) => void;
}

export function SavedViewsMenu({ views, onApply, onSave, onDelete }: SavedViewsMenuProps) {
  const [open, setOpen] = useState(false);
  const [name, setName] = useState('');
  const ref = useDismiss(open, () => setOpen(false));

  function submit() {
    const trimmed = name.trim();
    if (trimmed.length === 0) {
      return;
    }
    onSave(trimmed);
    setName('');
  }

  return (
    <div className="pop" ref={ref}>
      <Button aria-expanded={open} aria-haspopup="true" onClick={() => setOpen((o) => !o)}>
        Views{views.length > 0 ? ` (${views.length})` : ''}
      </Button>
      {open && (
        <div className="pop-panel" role="group" aria-label="Saved views">
          <div className="pop-cap">Saved views</div>
          {views.length === 0 && (
            <p className="muted" style={{ padding: '4px 8px', margin: 0, fontSize: 12 }}>
              No saved views yet.
            </p>
          )}
          {views.map((view) => (
            <div key={view.id} className="pop-row" style={{ padding: 0 }}>
              <button
                type="button"
                className="pop-row"
                onClick={() => {
                  onApply(view);
                  setOpen(false);
                }}
              >
                {view.name}
              </button>
              <button
                type="button"
                className="btn ghost ico sm"
                aria-label={`Delete view ${view.name}`}
                onClick={() => onDelete(view.id)}
              >
                ×
              </button>
            </div>
          ))}

          <div className="pop-cap">Save current view</div>
          <div style={{ display: 'flex', gap: 6, padding: '0 8px 6px' }}>
            <input
              className="fld"
              aria-label="View name"
              placeholder="e.g. Drafts to finish"
              value={name}
              onChange={(event) => setName(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter') {
                  event.preventDefault();
                  submit();
                }
              }}
            />
            <Button size="sm" variant="primary" onClick={submit} disabled={name.trim().length === 0}>
              Save
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}
