// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The translation editor: a lookup table of keys by language.
//
// Everything is addressed by key, because that is what the file is - the order rows sit in carries
// no meaning the user can see, which is also why the grid can be sorted freely and why adding an
// entry never asks where to put it.

import { useCallback, useEffect, useMemo, useState } from 'react';
import { createRoot } from 'react-dom/client';
import styled from 'styled-components';

import { BaselineSuggestion, suggestBaselineKeys } from './baselineSuggestions';
import { CellInput, menuAction, RowMenuFrame } from './loc/LocCells';
import { gridFooterLabel } from './loc/gridFooter';
import { LocGridMessage, LocGridShell } from './loc/LocGridShell';
import { LocProblemsBar } from './loc/LocProblemsBar';
import { LocRow } from './loc/locRow';
import { LocPanelMessage, LocProblem, post, useLocPanel } from './loc/useLocPanel';
import { severityIconFor, validateTitle } from './loc/validateState';
import { FilterMode, matchesFilter } from './locFilter';
import {
    BaselineEntryDto, BaselineRow, baselineValuesFor, findInheritedKeys, hideInherited,
    toBaselineRows,
} from './translationInherited';
import {
    blankDraft, draftToCommandValues, NewRowDraft, normaliseKey, validateNewKey,
} from './translationNewRow';
import { applyStaged, coalesce, TranslationCommand } from './translationStaging';
import { countDuplicateKeys, nextSort, sortRows, SortState } from './translationView';

function App(): React.JSX.Element {
    const [filter, setFilter] = useState('');
    const [mode, setMode] = useState<FilterMode>('text');
    const [scope, setScope] = useState('all');
    // View order only. Null is the order the file has on disk.
    const [sort, setSort] = useState<SortState | null>(null);
    // What the layers below this file already say. Entries merely repeating it are hidden by
    // default: a mod's text file is largely a copy of the game's, and the point is what it changes.
    const [baseline, setBaseline] = useState<BaselineRow[]>([]);
    const [showInherited, setShowInherited] = useState(false);
    // Open when adding an entry. The key is validated before the row exists, so there is never a
    // blank half-row in the grid waiting to be filled in.
    const [adding, setAdding] = useState(false);
    const [selected, setSelected] = useState<string | null>(null);
    // Set by an add; consumed once the entry exists so it can be scrolled to and focused.
    const [pendingFocus, setPendingFocus] = useState<number | null>(null);
    const [menu, setMenu] = useState<{ x: number; y: number; key: string; index: number } | null>(null);
    const [problemsOpen, setProblemsOpen] = useState(false);

    // A different file has a different baseline and a different order, so neither may linger and
    // decide what this one hides or how it is sorted.
    const onFileLoaded = useCallback(() => {
        setSort(null);
        setBaseline([]);
        setShowInherited(false);
        setSelected(null);
        post({ type: 'requestBaseline' });
    }, []);

    const onMessage = useCallback((msg: LocPanelMessage) => {
        if (msg.type === 'baselineRows') {
            setBaseline(toBaselineRows((msg.entries as BaselineEntryDto[]) ?? []));
        }
    }, []);

    const panel = useLocPanel<TranslationCommand>({
        applyStaged, coalesce, onFileLoaded, onMessage,
    });

    const {
        rows, languages, error, loaded, queue, problems, validation, stage, save, validate,
    } = panel;

    /** Adds a fully formed entry from the dialog. Position is never asked for. */
    const addEntry = useCallback((draft: NewRowDraft) => {
        const key = normaliseKey(draft.key);
        stage({ kind: 'addEntry', key, values: draftToCommandValues(draft) });

        // The new entry carries real content, but a filter or sort would still decide where - or
        // whether - it appears. Both are dropped so it is simply there, at the end, in view.
        setFilter('');
        setSort(null);
        setSelected(key);
        setPendingFocus(rows.length);
        setAdding(false);
    }, [rows.length, stage]);

    const deleteEntry = useCallback((key: string) => {
        stage({ kind: 'deleteEntry', key });
        setSelected(current => (current === key ? null : current));
    }, [stage]);

    /**
     * Puts an entry back to what the layer below it says, staged like any other edit so it is
     * undone by discarding rather than by a second round of typing.
     */
    const resetEntry = useCallback((key: string) => {
        const values = baselineValuesFor(key, baseline, languages);
        if (values === null) { return; }

        // The reset makes this entry inherited, so with inherited entries hidden it would drop off
        // screen the instant it was clicked - indistinguishable from the Delete directly beneath it
        // in the same menu. Showing them keeps it in place, dimmed, which is the actual result.
        setShowInherited(true);

        for (const value of values) {
            stage({ kind: 'setValue', key, language: value.language, value: value.value });
        }
    }, [baseline, languages, stage]);

    /** Entries identical to what a lower layer already provides. */
    const inherited = useMemo(
        () => findInheritedKeys(rows, baseline, languages),
        [rows, baseline, languages]);

    // Hide inherited, then filter, then order. All three are projections over the staged rows;
    // edits address entries by key, so none of them can move an edit onto the wrong entry.
    const visible = useMemo(
        () => sortRows(
            hideInherited(rows, inherited, showInherited)
                .filter(row => matchesFilter(row, filter, mode, scope)),
            sort),
        [rows, inherited, showInherited, filter, mode, scope, sort]);

    const problemKeys = useMemo(() => {
        const byKey = new Map<string, LocProblem>();
        for (const problem of problems) {
            if (problem.key) { byKey.set(problem.key, problem); }
        }
        return byKey;
    }, [problems]);

    // A fresh validation result reopens the bar; closing it is a decision about that result, not a
    // standing preference to never see the next one.
    useEffect(() => {
        if (problems.length > 0) { setProblemsOpen(true); }
    }, [problems]);

    /**
     * Takes the user to the entry a problem names.
     *
     * The entry may be hidden by the filter, or by inherited entries being hidden - jumping to
     * something that is not on screen would do nothing visible, so whatever is concealing it is
     * lifted first.
     */
    const jumpToProblem = useCallback((problem: LocProblem) => {
        if (!problem.key) { return null; }
        const target = rows.find(row => row.key === problem.key);
        if (target === undefined) { return null; }

        return () => {
            setFilter('');
            if (inherited.has(target.key)) { setShowInherited(true); }
            setSelected(target.key);
            setPendingFocus(target.index);
        };
    }, [rows, inherited]);

    // One template for the header and every row, so the columns cannot drift apart.
    const columns = useMemo(
        () => `260px ${languages.map(() => 'minmax(160px, 1fr)').join(' ')}`,
        [languages]);

    if (error) { return <LocGridMessage>{error}</LocGridMessage>; }
    if (!loaded) { return <LocGridMessage>Loading...</LocGridMessage>; }

    const duplicateCount = countDuplicateKeys(rows);
    // Two things can hold rows back here, and the footer says how many are doing so.
    const activeFilters =
        (filter.trim().length > 0 ? 1 : 0) + (!showInherited && inherited.size > 0 ? 1 : 0);

    const overlays = (
        <>
            {menu && (
                <TranslationRowMenu
                    x={menu.x}
                    y={menu.y}
                    index={menu.index}
                    onClose={() => setMenu(null)}
                    onAdd={() => setAdding(true)}
                    // Empty when the click missed every row, which is the only way into an empty
                    // file - there is nothing to delete or reset there.
                    onDelete={menu.key === '' ? null : () => deleteEntry(menu.key)}
                    onReset={menu.key !== ''
                        && baselineValuesFor(menu.key, baseline, languages) !== null
                        && !inherited.has(menu.key)
                        ? () => resetEntry(menu.key)
                        : null}
                />
            )}
            {adding && (
                <AddTranslationDialog
                    languages={languages}
                    existingKeys={rows.map(row => row.key)}
                    baseline={baseline}
                    onCancel={() => setAdding(false)}
                    onAdd={addEntry}
                />
            )}
        </>
    );

    const header = (
        <>
            <HeadCell label="Key" column="key" sort={sort} onSort={setSort} />
            {languages.map(language => (
                <HeadCell
                    key={language}
                    label={language}
                    column={language}
                    sort={sort}
                    onSort={setSort}
                />
            ))}
        </>
    );

    const renderRow = (row: LocRow): React.JSX.Element => (
        <>
            <div className="cell">
                <CellInput
                    value={row.key}
                    onCommit={next => stage({ kind: 'renameKey', key: row.key, newKey: next })}
                />
            </div>
            {languages.map(language => (
                <div className="cell" key={language}>
                    <CellInput
                        value={valueOf(row, language)}
                        onCommit={next => stage({
                            kind: 'setValue', key: row.key, language, value: next,
                        })}
                    />
                </div>
            ))}
        </>
    );

    // Laid out like the story graph editor's: Save pinned left, Validate a soft severity pill
    // pinned right, both icon-first.
    const severityIcon = severityIconFor(validation);

    const dockHeader = (
        <>
            <button
                className={`icon-btn header-left${queue.length > 0 ? ' active' : ''}`}
                disabled={queue.length === 0}
                onClick={save}
                title="Save - write all staged changes to the file"
            >
                <span className="codicon codicon-save" />
                {queue.length > 0 ? ` ${queue.length}` : ''}
            </button>
            <button
                className={`icon-btn validate-btn header-right sev-${validation}`}
                onClick={validate}
                title={validateTitle(validation, problems.length, queue.length)}
            >
                <span className={`codicon codicon-${severityIcon}`} />
                {problems.length ? ` ${problems.length}` : ''}
            </button>
        </>
    );

    const dockContent = (
        <>
            {/* A repeated key is an error the validator reports: only one of the two would ever be
                read. Row counts live under the table, where they describe it. */}
            {duplicateCount > 0 && (
                <div className="counts">
                    <div className="warn">{duplicateCount} duplicate keys</div>
                </div>
            )}

        </>
    );

    const dockOverview = (
        <>
            <input
                type="text"
                className="filter"
                placeholder={placeholderFor(mode)}
                value={filter}
                onChange={e => setFilter(e.target.value)}
            />

            <div className="mode-group">
                {(['text', 'wildcard', 'regex'] as FilterMode[]).map(m => (
                    <button
                        key={m}
                        className={`icon-btn${mode === m ? ' active' : ''}`}
                        title={titleFor(m)}
                        aria-label={m}
                        aria-pressed={mode === m}
                        onClick={() => setMode(m)}
                    >
                        <span className={`codicon codicon-${iconFor(m)}`} />
                    </button>
                ))}
            </div>

            <div className="filters-below">
                <select value={scope} onChange={e => setScope(e.target.value)} title="Search in">
                    <option value="all">All fields</option>
                    <option value="key">Key only</option>
                    {languages.map(language => (
                        <option key={language} value={language}>{language}</option>
                    ))}
                </select>

                <label
                    className="inherited-toggle"
                    title="Show entries whose value is identical to the layer below this file"
                >
                    <input
                        type="checkbox"
                        checked={showInherited}
                        onChange={e => setShowInherited(e.target.checked)}
                    />
                    Inherited
                    {inherited.size > 0 && <span className="badge">{inherited.size}</span>}
                </label>
            </div>
        </>
    );

    return (
        <LocGridShell
            rows={visible}
            columns={columns}
            header={header}
            renderRow={renderRow}
            rowClassName={row => [
                problemKeys.has(row.key) ? 'has-problem' : '',
                row.key === selected ? 'selected' : '',
                // Only visible when showing them, since they are otherwise filtered out entirely.
                inherited.has(row.key) ? 'inherited' : '',
            ].filter(Boolean).join(' ')}
            rowTitle={row => problemKeys.get(row.key)?.message}
            onRowFocus={row => setSelected(row.key)}
            onRowContextMenu={(row, e) => {
                e.preventDefault();
                setSelected(row.key);
                setMenu({ x: e.clientX, y: e.clientY, key: row.key, index: row.index });
            }}
            focusRowIndex={pendingFocus}
            onFocused={() => setPendingFocus(null)}
            overlays={overlays}
            problemsBar={problems.length > 0 && problemsOpen ? (
                <LocProblemsBar
                    problems={problems}
                    labelOf={problem => problem.key ?? 'This file'}
                    jumpTo={jumpToProblem}
                    onClose={() => setProblemsOpen(false)}
                />
            ) : undefined}
            footer={gridFooterLabel(rows.length, visible.length, activeFilters)}
            onEmptyAreaContextMenu={e => {
                e.preventDefault();
                setMenu({ x: e.clientX, y: e.clientY, key: '', index: -1 });
            }}
            dockHeader={dockHeader}
            dockContent={dockContent}
            dockOverview={dockOverview}
        />
    );
}

/**
 * Right-click menu for a translation entry.
 *
 * There is no insert-above or insert-below: position means nothing here, so there would be nothing
 * to choose between them. Add opens a dialog that settles the key before the entry exists.
 */
function TranslationRowMenu(props: {
    x: number; y: number; index: number;
    onClose: () => void;
    onAdd: () => void;
    /** Null when the menu was opened on empty space rather than on an entry. */
    onDelete: (() => void) | null;
    /** Null when the entry has no inherited value to go back to, or already matches it. */
    onReset: (() => void) | null;
}): React.JSX.Element {
    const run = (action: () => void) => menuAction(action, props.onClose);

    return (
        <RowMenuFrame x={props.x} y={props.y} anchorIndex={props.index} onClose={props.onClose}>
            <button role="menuitem" onClick={run(props.onAdd)}>Add translation...</button>

            {props.onReset && (
                <>
                    <div className="sep" />
                    <button
                        role="menuitem"
                        title="Replace this entry with the value the layer below it provides"
                        onClick={run(() => props.onReset?.())}
                    >
                        Reset to inherited value
                    </button>
                </>
            )}

            {props.onDelete && (
                <>
                    <div className="sep" />
                    <button
                        role="menuitem"
                        className="danger"
                        onClick={run(() => props.onDelete?.())}
                    >
                        Delete row
                    </button>
                </>
            )}
        </RowMenuFrame>
    );
}

/**
 * A column header, clickable to sort.
 *
 * Sorting a language column puts the untranslated entries together, which is the quickest way to
 * see what is still missing.
 */
function HeadCell(props: {
    label: string;
    column: string;
    sort: SortState | null;
    onSort: (next: SortState | null) => void;
}): React.JSX.Element {
    const active = props.sort?.column === props.column ? props.sort : null;

    return (
        <div
            className={`cell sortable${active ? ' sorted' : ''}`}
            role="columnheader"
            tabIndex={0}
            aria-sort={active === null ? 'none' : active.direction === 'asc' ? 'ascending' : 'descending'}
            title={`Sort by ${props.label}`}
            onClick={() => props.onSort(nextSort(props.sort, props.column))}
            onKeyDown={e => {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    props.onSort(nextSort(props.sort, props.column));
                }
            }}
        >
            <span className="head-label">{props.label}</span>
            {active && (
                <span
                    className={`codicon codicon-arrow-${active.direction === 'asc' ? 'up' : 'down'}`}
                    aria-hidden="true"
                />
            )}
        </div>
    );
}

/**
 * Collects a whole translation before it becomes an entry.
 *
 * The alternative - dropping an empty row into the grid and letting it be filled in - leaves an
 * entry with no key in the file, which is exactly what the batch validator rejects, and gives no
 * indication that the key is required or already taken until Save.
 */
function AddTranslationDialog(props: {
    languages: string[];
    existingKeys: string[];
    baseline: BaselineRow[];
    onCancel: () => void;
    onAdd: (draft: NewRowDraft) => void;
}): React.JSX.Element {
    const [draft, setDraft] = useState<NewRowDraft>(() => blankDraft(props.languages));
    // The key error is shown once the field has been visited, so the dialog does not open already
    // complaining about a field nobody has touched.
    const [touched, setTouched] = useState(false);
    // Dismissed once a suggestion is taken, so the list does not reappear over the filled-in form.
    const [picked, setPicked] = useState(false);

    const error = validateNewKey(draft.key, props.existingKeys);

    const suggestions = useMemo(
        () => (picked
            ? []
            : suggestBaselineKeys(draft.key, props.baseline, props.existingKeys, props.languages)),
        [picked, draft.key, props.baseline, props.existingKeys, props.languages]);

    const submit = (): void => {
        setTouched(true);
        if (error === null) { props.onAdd(draft); }
    };

    const setValue = (language: string, value: string): void => setDraft(current => ({
        ...current,
        values: current.values.map(v => (v.language === language ? { language, value } : v)),
    }));

    /** Takes a suggestion: its key, and the inherited text as a starting point for editing. */
    const take = (suggestion: BaselineSuggestion): void => {
        setDraft({ key: suggestion.key, values: suggestion.values });
        setPicked(true);
    };

    return (
        <Backdrop onPointerDown={e => { if (e.target === e.currentTarget) { props.onCancel(); } }}>
            <form
                className="dialog"
                role="dialog"
                aria-modal="true"
                aria-label="Add translation"
                onSubmit={e => { e.preventDefault(); submit(); }}
                onKeyDown={e => { if (e.key === 'Escape') { props.onCancel(); } }}
            >
                <h2>Add translation</h2>

                <label className="field">
                    <span>Key</span>
                    <input
                        type="text"
                        value={draft.key}
                        autoFocus
                        spellCheck={false}
                        aria-invalid={touched && error !== null}
                        onChange={e => {
                            setPicked(false);
                            setDraft(current => ({ ...current, key: e.target.value }));
                        }}
                        onBlur={() => setTouched(true)}
                    />
                </label>
                {touched && error !== null && <p className="field-error">{error}</p>}

                {/* Keys the layers below define but this file does not - which is exactly what an
                    override is. Taking one fills in the inherited text to edit from. */}
                {suggestions.length > 0 && (
                    <ul className="suggestions">
                        {suggestions.map(suggestion => (
                            <li key={suggestion.key}>
                                <button type="button" onClick={() => take(suggestion)}>
                                    <span className="suggestion-key">{suggestion.key}</span>
                                    <span className="suggestion-value">
                                        {suggestion.values.find(v => v.value)?.value ?? ''}
                                    </span>
                                </button>
                            </li>
                        ))}
                    </ul>
                )}

                <div className="languages">
                    {props.languages.map(language => (
                        <label className="field" key={language}>
                            <span>{language}</span>
                            <input
                                type="text"
                                value={draft.values.find(v => v.language === language)?.value ?? ''}
                                onChange={e => setValue(language, e.target.value)}
                            />
                        </label>
                    ))}
                </div>

                {/* Not required: a key with no text yet is a legitimate thing to add, and the
                    untranslated cells are visible in the grid afterwards. */}
                <p className="hint">Languages you leave empty stay empty.</p>

                <div className="dialog-actions">
                    <button type="button" onClick={props.onCancel}>Cancel</button>
                    <button type="submit" className="primary" disabled={error !== null}>Add</button>
                </div>
            </form>
        </Backdrop>
    );
}

function valueOf(row: LocRow, language: string): string {
    return row.values.find(v => v.language === language)?.value ?? '';
}

function iconFor(mode: FilterMode): string {
    return mode === 'text' ? 'case-sensitive' : mode === 'wildcard' ? 'star-full' : 'regex';
}

function titleFor(mode: FilterMode): string {
    return mode === 'text' ? 'Plain text search'
        : mode === 'wildcard' ? 'Wildcard: * matches any text, ? matches one character'
            : 'Regular expression (case-insensitive)';
}

function placeholderFor(mode: FilterMode): string {
    return mode === 'wildcard' ? 'TEXT_UNIT_*' : mode === 'regex' ? '^TEXT_.*' : 'Filter rows...';
}

// Covers the whole editor rather than the grid alone, so the dialog cannot be scrolled away from
// or edited around while it is open.
const Backdrop = styled.div`
    position: absolute;
    inset: 0;
    z-index: 20;
    display: flex;
    align-items: center;
    justify-content: center;
    background: rgba(0, 0, 0, 0.45);

    .dialog {
        min-width: 380px;
        max-width: min(560px, 90vw);
        max-height: 85vh;
        overflow-y: auto;
        display: flex;
        flex-direction: column;
        gap: 10px;
        padding: 16px 18px;
        background: var(--vscode-editorWidget-background, #252526);
        border: 1px solid var(--vscode-widget-border, var(--vscode-panel-border, #454545));
        border-radius: 4px;
        box-shadow: 0 4px 16px rgba(0, 0, 0, 0.4);
    }

    h2 { margin: 0; font-size: 1.1em; font-weight: 600; }

    .field { display: flex; flex-direction: column; gap: 3px; }
    .field > span { opacity: 0.85; font-size: 0.9em; }

    .field input {
        background: var(--vscode-input-background, #3c3c3c);
        color: var(--vscode-input-foreground, #ccc);
        border: 1px solid var(--vscode-input-border, transparent);
        padding: 4px 6px;
        font: inherit;
    }

    .field input:focus {
        outline: 1px solid var(--vscode-focusBorder, #007fd4);
        outline-offset: -1px;
    }

    .field input[aria-invalid='true'] {
        border-color: var(--vscode-inputValidation-errorBorder, #be1100);
    }

    .field-error {
        margin: -4px 0 0;
        color: var(--vscode-inputValidation-errorForeground, var(--vscode-errorForeground, #f48771));
        font-size: 0.9em;
    }

    /* Scrolls on its own once a project declares more languages than fit - the key field and the
       buttons stay put. */
    .languages {
        display: flex;
        flex-direction: column;
        gap: 8px;
        max-height: 40vh;
        overflow-y: auto;
        padding-top: 4px;
        border-top: 1px solid var(--vscode-panel-border, #444);
    }

    .hint { margin: 0; opacity: 0.6; font-size: 0.9em; }

    /* Sits directly under the key field, capped so a broad prefix cannot push the buttons off the
       dialog. */
    .suggestions {
        list-style: none;
        margin: -4px 0 0;
        padding: 0;
        max-height: 30vh;
        overflow-y: auto;
        border: 1px solid var(--vscode-panel-border, #444);
    }

    .suggestions button {
        display: flex;
        justify-content: space-between;
        gap: 12px;
        width: 100%;
        padding: 3px 6px;
        background: none;
        border: none;
        color: inherit;
        font: inherit;
        text-align: left;
        cursor: pointer;
    }

    .suggestions button:hover, .suggestions button:focus-visible {
        background: var(--vscode-list-hoverBackground, #2a2d2e);
        outline: none;
    }

    .suggestion-key { white-space: nowrap; }
    .suggestion-value {
        opacity: 0.6;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }

    .dialog-actions { display: flex; justify-content: flex-end; gap: 6px; }

    .dialog-actions button {
        background: var(--vscode-button-secondaryBackground, #3a3d41);
        color: var(--vscode-button-secondaryForeground, #ccc);
        border: none;
        padding: 4px 14px;
        font: inherit;
        cursor: pointer;
    }

    .dialog-actions .primary {
        background: var(--vscode-button-background, #0e639c);
        color: var(--vscode-button-foreground, #fff);
    }

    .dialog-actions button:disabled { opacity: 0.5; cursor: default; }
`;

createRoot(document.getElementById('root')!).render(<App />);
