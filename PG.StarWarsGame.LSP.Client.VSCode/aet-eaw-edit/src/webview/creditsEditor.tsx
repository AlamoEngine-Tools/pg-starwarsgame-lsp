// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The credits editor: an ordered list of rows, addressed by position.
//
// A credits file is a running order the crawl plays top to bottom. The same key repeats on hundreds
// of rows because it is a formatting directive rather than an identifier, and blank rows are
// content. None of that can be named by key, which is why this editor and the translation editor
// are separate programs over a shared grid rather than one grid deciding what kind of file it has.

import { useCallback, useEffect, useMemo, useState } from 'react';
import { createRoot } from 'react-dom/client';

import { CreditsCrawl } from './creditsCrawl';
import { BLANK_SENTINEL_VALUE, CREDITS_DIRECTIVES, isSpacerRow } from './creditsCrawlModel';
import { applyStaged, coalesce, LocCommand } from './creditsStaging';
import {
    CREDITS_STEPS, CreditsStep, directiveOptionText, DropRow, DropTarget, dropTargetAt, stepById,
} from './creditsSteps';
import { CellInput, menuAction, RowMenuFrame } from './loc/LocCells';
import { gridFooterLabel } from './loc/gridFooter';
import { ConvertibleFormat, LocDockActions } from './loc/LocDockActions';
import { LocGridMessage, LocGridShell } from './loc/LocGridShell';
import { LocColumnMenu } from './loc/LocColumnMenu';
import { LocDockHeader } from './loc/LocDockHeader';
import { useLocColumns } from './loc/useLocColumns';
import { creditsBaselineValues } from './loc/creditsBaselineFill';
import { creditsBaselineSeed, CreditsSeedRow, creditsFileSeed } from './loc/creditsSeed';
import { BaselineEntryDto, BaselineRow, toBaselineRows } from './translationInherited';
import { languageCopyValues } from './loc/languageCopy';
import { LocTile, LocTileGrid } from './loc/LocTile';
import { LocSearch } from './loc/LocSearch';
import { LocProblemsBar } from './loc/LocProblemsBar';
import { LocRow } from './loc/locRow';
import { rowSeverityClass, severityByRow } from './loc/rowSeverity';
import { FILTER_DEBOUNCE_MS, useDebounced } from './loc/useDebounced';
import { LocPanelMessage, LocProblem, post, useLocPanel } from './loc/useLocPanel';
import { DockSection } from './shared/DockSection';
import { buildRowFilter, FilterMode } from './locFilter';

/**
 * The drag payload: which kind of line is being placed.
 *
 * A custom type rather than `text/plain`, so text dragged in from anywhere else is not mistaken for
 * a step and dropped into the file as a row.
 */
const STEP_MIME = 'application/x-aet-credits-step';

/**
 * Another language file of this project, offered as a starting point for a file with nothing in it.
 *
 * Sent by the panel only when this file is empty - see LocalisationEditorPanel._sendSeedSources.
 * It carries the rows, so picking one stages the copy locally like any other edit: nothing reaches
 * disk until Save, and discarding puts the file back to empty.
 */
interface SeedSource {
    filePath: string;
    label: string;
    language: string;
    rowCount: number;
    rows: LocRow[];
}

function App(): React.JSX.Element {
    const [filter, setFilter] = useState('');
    // What the box shows is immediate; what the grid applies waits for typing to settle.
    const appliedFilter = useDebounced(filter, FILTER_DEBOUNCE_MS);
    const [mode, setMode] = useState<FilterMode>('text');
    const [scope, setScope] = useState('all');
    // Set when the crawl is playing over the editor itself, which is what the full-screen button
    // does; the plain preview button opens a panel beside instead.
    const [previewing, setPreviewing] = useState(false);
    // True once a side-by-side preview is open, so later edits keep pushing rows to it.
    const [previewOpen, setPreviewOpen] = useState(false);
    const [pendingCrawl, setPendingCrawl] = useState<'beside' | 'fullScreen' | null>(null);

    // Driven from the editor title bar, where a preview button belongs and where Markdown and
    // LaTeX editors already put theirs.
    const onMessage = useCallback((msg: LocPanelMessage) => {
        if (msg.type === 'baselineRows') {
            setBaseline(toBaselineRows((msg.entries as BaselineEntryDto[]) ?? []));
            return;
        }

        if (msg.type === 'seedSources') {
            setSeedSources((msg.sources as SeedSource[]) ?? []);
            return;
        }

        if (msg.type !== 'requestCrawlRows') { return; }
        setPendingCrawl(msg.fullScreen ? 'fullScreen' : 'beside');
    }, []);

    // The game's own credits, for filling a language from what it already ships. Requested per
    // file: the server answers with the credits baseline for a credits file and the MasterText one
    // for a text file, so which arrives depends on what is open.
    const [baseline, setBaseline] = useState<BaselineRow[]>([]);
    // Null means "the user has not chosen", in which case the languages this file says nothing in
    // are hidden - see the translation editor for the same rule.
    const [hiddenLanguages, setHiddenLanguages] = useState<Set<string> | null>(null);
    // Only ever non-empty for a file with nothing in it; the panel decides and sends them unasked.
    const [seedSources, setSeedSources] = useState<SeedSource[]>([]);
    const onFileLoaded = useCallback(() => {
        setBaseline([]);
        setHiddenLanguages(null);
        // A different file has different siblings, and the panel re-sends them if they apply.
        setSeedSources([]);
        post({ type: 'requestBaseline' });
    }, []);
    // The row the caret last sat in. "Insert above/below" is meaningless without it: here position
    // is the content, so the user has to be able to say where.
    const [selected, setSelected] = useState<number | null>(null);
    // Set by an insert; consumed once the new row exists so it can be scrolled to and focused.
    const [pendingFocus, setPendingFocus] = useState<number | null>(null);
    // Row operations live on a right-click menu rather than in the dock: they act on a row, so the
    // row is where they belong, and the dock stays about the file as a whole.
    const [menu, setMenu] = useState<{ x: number; y: number; index: number } | null>(null);
    const [problemsOpen, setProblemsOpen] = useState(false);

    const panel = useLocPanel<LocCommand>({ applyStaged, coalesce, onFileLoaded, onMessage });
    const {
        rows, languages, setLanguages, canAddLanguage, addLanguageCreatesFile,
        supportedLanguages, error, loaded, queue,
        problems, validation, stage, save, validate,
    } = panel;

    /**
     * Inserts a row and takes the user to it.
     *
     * `where` is relative to the row the action was invoked on. An ordered file is a running list,
     * so appending at the end is rarely what was meant.
     */
    const insertRow = useCallback((
        where: 'above' | 'below' | 'end', value = '', anchor?: number,
    ) => {
        // The anchor is the row the action was invoked on. It is passed explicitly because the
        // right-click menu targets the row under the pointer, which is not necessarily the row the
        // caret was last in - relying on the selection put inserts on the wrong line.
        const target = anchor ?? selected;
        const at = where === 'end' || target === null
            ? rows.length
            : where === 'above' ? target : target + 1;

        stage({
            kind: 'insertRow',
            index: at,
            key: CREDITS_DIRECTIVES[0],
            values: languages.map(language => ({ language, value })),
        });

        // A new row is empty, so an active filter would hide the thing just created.
        setFilter('');
        setSelected(at);
        setPendingFocus(at);
    }, [rows.length, selected, languages, stage]);

    /**
     * Drops a step from the library at a position.
     *
     * Unlike the menu's insert, the position is wherever it was dropped rather than relative to a
     * row that was clicked, so it is passed as an absolute index.
     */
    const insertStep = useCallback((step: CreditsStep, at: number) => {
        stage({
            kind: 'insertRow',
            index: at,
            key: step.key,
            values: languages.map(language => ({ language, value: step.value })),
        });

        // The new row is empty, so an active filter would hide the thing just placed.
        setFilter('');
        setSelected(at);
        setPendingFocus(at);
    }, [languages, stage]);

    const deleteRow = useCallback((index: number) => {
        stage({ kind: 'deleteRow', index });
        setSelected(current => (current === null || current < index ? current
            : current === index ? null : current - 1));
    }, [stage]);

    const moveRow = useCallback((index: number, to: number) => {
        stage({ kind: 'moveRow', index, toIndex: to });
        setSelected(to);
        setPendingFocus(to);
    }, [stage]);

    // Compiled once per filter change rather than once per row - see buildRowFilter.
    const rowFilter = useMemo(
        () => buildRowFilter(appliedFilter, mode, scope), [appliedFilter, mode, scope]);

    const visible = useMemo(() => rows.filter(rowFilter.test), [rows, rowFilter]);

    const crawlLanguage = scope !== 'all' && scope !== 'key' ? scope : languages[0] ?? '';

    // Answers the title bar's request for rows. Deferred through state rather than sent from the
    // message handler so it always sends the current staged rows rather than the ones captured when
    // the listener was bound.
    useEffect(() => {
        if (pendingCrawl === null) { return; }

        if (pendingCrawl === 'fullScreen') {
            setPreviewing(true);
            void requestFullScreen();
        } else {
            post({
                type: 'crawlRows', open: true, rows, languages, language: crawlLanguage,
            });
            setPreviewOpen(true);
        }

        setPendingCrawl(null);
    }, [pendingCrawl, rows, languages, crawlLanguage]);

    // A preview beside shows what is staged, so it follows the edits rather than the file.
    useEffect(() => {
        if (!previewOpen) { return; }
        post({ type: 'crawlRows', open: false, rows, languages, language: crawlLanguage });
    }, [previewOpen, rows, languages, crawlLanguage]);

    // A fresh validation result reopens the bar; closing it is a decision about that result, not a
    // standing preference to never see the next one.
    useEffect(() => {
        if (problems.length > 0) { setProblemsOpen(true); }
    }, [problems]);

    /** Takes the user to the row a problem names, lifting any filter concealing it. */
    const jumpToProblem = useCallback((problem: LocProblem) => {
        const index = problem.index;
        if (index === null || index === undefined || rows[index] === undefined) { return null; }

        return () => {
            setFilter('');
            setSelected(index);
            setPendingFocus(index);
        };
    }, [rows]);

    // Where a dragged step would land, and the line marking it. Null when nothing is over the grid.
    const [dropTarget, setDropTarget] = useState<DropTarget | null>(null);

    /** Row boundaries as the browser currently has them - the drag has to hit-test against what is
     *  on screen, and the grid is virtualised so only those rows exist. */
    const rowBoundaries = (): DropRow[] => [...document.querySelectorAll('.data-row')]
        .map(el => {
            const box = el.getBoundingClientRect();
            return {
                index: Number(el.getAttribute('data-row-index')),
                top: box.top,
                bottom: box.bottom,
            };
        })
        .sort((a, b) => a.top - b.top);

    const onGridDragOver = useCallback((e: React.DragEvent) => {
        if (!e.dataTransfer.types.includes(STEP_MIME)) { return; }
        // Without this the browser refuses the drop and shows a "no entry" cursor.
        e.preventDefault();
        e.dataTransfer.dropEffect = 'copy';
        setDropTarget(dropTargetAt(e.clientY, rowBoundaries(), rows.length));
    }, [rows.length]);

    const onGridDrop = useCallback((e: React.DragEvent) => {
        const step = stepById(e.dataTransfer.getData(STEP_MIME));
        if (step === null) { return; }
        e.preventDefault();

        const target = dropTargetAt(e.clientY, rowBoundaries(), rows.length);
        setDropTarget(null);
        insertStep(step, target.index);
    }, [rows.length, insertStep]);

    // See the translation editor: keyed by row index so the grid can tint per row.
    const rowSeverities = useMemo(() => severityByRow(problems, rows), [problems, rows]);

    const problemRows = useMemo(() => {
        const byIndex = new Map<number, LocProblem>();
        for (const problem of problems) {
            if (problem.index !== null && problem.index !== undefined) {
                byIndex.set(problem.index, problem);
            }
        }
        return byIndex;
    }, [problems]);

    // See the translation editor: hiding the column the search is scoped to would leave the grid
    // filtered by something the user can no longer see.
    const onColumnHidden = useCallback(
        (language: string) => setScope(s => (s === language ? 'all' : s)), []);

    // No focusLanguage: a credits set is never opened one language at a time, since credits stay
    // one file per tab - their rows are addressed by position, so merging them by key is not
    // defined. The default here is therefore always "hide the empty columns".
    const { hidden, shownLanguages, columns, toggleLanguage, materialise } = useLocColumns({
        rows, languages, hiddenLanguages, setHiddenLanguages, onHidden: onColumnHidden,
    });

    // Nothing on screen is what "empty" means to whoever is looking at it, whatever the file
    // happens to hold. It is also the one state in which seeding cannot overwrite anything.
    const isEmptyFile = rows.length === 0;

    /**
     * Turns seed rows into staged inserts.
     *
     * Appended one at a time: each insert lands at the end of what is there so far, and the running
     * order IS the content of a credits file. Staged like any other edit, so nothing reaches disk
     * until Save and discarding puts the file back to empty.
     */
    const seedInto = (seeds: CreditsSeedRow[], language: string): void =>
        seeds.forEach((seed, at) => stage({
            kind: 'insertRow',
            index: at,
            key: seed.key,
            values: languages.map(l => ({ language: l, value: l === language ? seed.value : '' })),
        }));

    if (error) { return <LocGridMessage>{error}</LocGridMessage>; }
    if (!loaded) { return <LocGridMessage>Loading...</LocGridMessage>; }

    const overlays = (
        <>
            {menu && (
                <CreditsRowMenu
                    x={menu.x}
                    y={menu.y}
                    index={menu.index}
                    spacer={rows[menu.index] !== undefined && isSpacerRow(rows[menu.index])}
                    lastIndex={rows.length - 1}
                    onClose={() => setMenu(null)}
                    onInsert={where => insertRow(where, '', menu.index)}
                    onInsertSpacer={where => insertRow(where, BLANK_SENTINEL_VALUE, menu.index)}
                    onDelete={index => deleteRow(index)}
                    onMove={(index, to) => moveRow(index, to)}
                />
            )}
            {previewing && (
                <CreditsCrawl
                    rows={rows}
                    languages={languages}
                    initialLanguage={crawlLanguage}
                    onClose={() => {
                        setPreviewing(false);
                        // Leaving the preview leaves full screen with it, so Esc does not strand
                        // the user in a chromeless window.
                        if (document.fullscreenElement) { void document.exitFullscreen(); }
                    }}
                />
            )}
        </>
    );

    // No sorting: the running order is the content, so there is no view to re-order into.
    const header = (
        <>
            {/* The key column holds a formatting instruction, not an identifier - calling it "Key"
                would misdescribe it. */}
            <div className="cell">Format</div>
            {shownLanguages.map(language => <div className="cell" key={language}>{language}</div>)}
            <LocColumnMenu languages={languages} hidden={hidden} onToggle={toggleLanguage} />
        </>
    );

    const renderRow = (row: LocRow): React.JSX.Element => (isSpacerRow(row)
        ? (
            // Nothing to edit: a spacer is a marker. Shown as one, spanning the row, and removed
            // rather than typed over.
            <div className="cell spacer-cell" tabIndex={0}>
                <span className="spacer-rule" />
                <span className="spacer-label">blank line</span>
                <span className="spacer-rule" />
            </div>
        )
        : (
            <>
                <div className="cell">
                    <DirectiveSelect
                        value={row.key}
                        onCommit={next => stage({ kind: 'setKey', index: row.index, key: next })}
                    />
                </div>
                {shownLanguages.map(language => (
                    <div className="cell" key={language}>
                        <CellInput
                            value={valueOf(row, language)}
                            onCommit={next => stage({
                                kind: 'setCell',
                                index: row.index,
                                language,
                                value: next,
                                // Lets the server refuse rather than edit the wrong row if the two
                                // views of row order have drifted.
                                expectedKey: row.key,
                            })}
                        />
                    </div>
                ))}
            </>
        ));

    const dockHeader = (
        <LocDockHeader
            queue={queue}
            problems={problems}
            validation={validation}
            onSave={save}
            onValidate={validate}
        />
    );

    const dockContent = (
        <>
            <LocDockActions
                languages={languages}
                canAddLanguage={canAddLanguage}
                addLanguageCreatesFile={addLanguageCreatesFile}
                supportedLanguages={supportedLanguages}
                rowCount={rows.length}
                onAddLanguage={(language, fillFromBaseline) => {
                    // A single-language format cannot take a column - the language goes in a new
                    // file beside this one, which the host has to create because it is not an edit
                    // to this document and cannot be staged with the rest of the batch.
                    if (addLanguageCreatesFile) {
                        post({ type: 'addLanguageFile', language });
                        return;
                    }

                    stage({ kind: 'addLanguage', language });
                    setLanguages(current => [...current, language]);

                    // Matched by text, since a credits key names a format and not an entry.
                    if (fillFromBaseline) {
                        for (const value of creditsBaselineValues(language, rows, baseline)) {
                            stage({ kind: 'setCell', index: value.index, language, value: value.value });
                        }
                    }
                    // Explicit from here on, so the column just added is not hidden for being empty.
                    materialise();
                }}
                copyLanguageCount={(from, to) => languageCopyValues(from, to, rows).length}
                onCopyLanguage={(from, to) => {
                    // Positional here: a credits row is addressed by where it sits, and its key is
                    // a formatting directive that several rows share.
                    for (const copied of languageCopyValues(from, to, rows)) {
                        stage({ kind: 'setCell', index: copied.index, language: to, value: copied.value });
                    }
                }}
                // An empty table is seeded, a populated one is filled - see fillFromBaseline.
                baselineFillCount={language => (isEmptyFile
                    ? creditsBaselineSeed(language, baseline).length
                    : creditsBaselineValues(language, rows, baseline).length)}
                fillsByAdding={isEmptyFile}
                seedSources={seedSources}
                onSeedFrom={(filePath, language) => {
                    const source = seedSources.find(s => s.filePath === filePath);
                    if (source === undefined) { return; }
                    seedInto(creditsFileSeed(source.rows, source.language), language);
                }}
                // Offered when the file is empty, or when a second language is present to match
                // lines by. Absent otherwise, which hides the Fill tile: credits are matched by
                // text, so filling a language a populated file already has needs another language
                // to identify each line by - the target's own text is what is being replaced. With
                // one language and rows on screen it could never fill anything, and the dialog only
                // said so after opening it: "there is nothing left to fill in", every time.
                onFillFromBaseline={!isEmptyFile && languages.length < 2 ? undefined : language => {
                    if (isEmptyFile) {
                        seedInto(creditsBaselineSeed(language, baseline), language);
                        return;
                    }

                    for (const value of creditsBaselineValues(language, rows, baseline)) {
                        stage({ kind: 'setCell', index: value.index, language, value: value.value });
                    }
                }}
                onConvertFormat={(format: ConvertibleFormat) =>
                    post({ type: 'convertFormat', targetFormat: format })}
                onExportDat={() => post({ type: 'exportDat' })}
            />

            {/* The three kinds of line the format is built from, as things to pick up and place.
                Dropping says where far more directly than choosing "insert above" does. On the same
                tile grid as the actions above: two tile species in one dock read as two unrelated
                control sets. */}
            <DockSection title="Content elements">
                <LocTileGrid>
                    {CREDITS_STEPS.map(step => (
                        <LocTile
                            key={step.id}
                            icon={step.codicon}
                            label={step.label}
                            // The token this becomes in the file, so the tile and the Format column
                            // are visibly the same thing.
                            badge={step.token}
                            title={`Drag onto the table, or click to add at the end - ${step.description}`}
                            draggable
                            onDragStart={e => {
                                e.dataTransfer.setData(STEP_MIME, step.id);
                                e.dataTransfer.effectAllowed = 'copy';
                            }}
                            onDragEnd={() => setDropTarget(null)}
                            // Clicking appends, so the library works without dragging at all.
                            onClick={() => insertStep(step, rows.length)}
                        />
                    ))}
                </LocTileGrid>
                <p className="step-help">Drag onto the table to place, or click to add at the end.</p>
            </DockSection>
        </>
    );

    const dockOverview = (
        <LocSearch
            filter={filter}
            onFilter={setFilter}
            mode={mode}
            onMode={setMode}
            scope={scope}
            onScope={setScope}
            languages={languages}
            error={rowFilter.error}
            placeholders={PLACEHOLDERS}
            keyScopeLabel="Format only"
        />
    );

    return (
        <LocGridShell
            rows={visible}
            columns={columns}
            header={header}
            renderRow={renderRow}
            rowClassName={row => [
                rowSeverityClass(rowSeverities.get(row.index)) ?? '',
                row.index === selected ? 'selected' : '',
            ].filter(Boolean).join(' ')}
            rowTitle={row => problemRows.get(row.index)?.message}
            onRowFocus={row => setSelected(row.index)}
            onRowContextMenu={(row, e) => {
                e.preventDefault();
                setSelected(row.index);
                setMenu({ x: e.clientX, y: e.clientY, index: row.index });
            }}
            focusRowIndex={pendingFocus}
            onFocused={() => setPendingFocus(null)}
            overlays={overlays}
            problemsBar={problems.length > 0 && problemsOpen ? (
                <LocProblemsBar
                    problems={problems}
                    labelOf={problem => (problem.index === null || problem.index === undefined
                        ? 'This file'
                        : `Row ${problem.index + 1}`)}
                    jumpTo={jumpToProblem}
                    onClose={() => setProblemsOpen(false)}
                />
            ) : undefined}
            footer={gridFooterLabel(
                rows.length, visible.length, filter.trim().length > 0 ? 1 : 0)}
            onGridDragOver={onGridDragOver}
            onGridDrop={onGridDrop}
            onGridDragLeave={() => setDropTarget(null)}
            dropIndicatorY={dropTarget?.y ?? null}
            onEmptyAreaContextMenu={e => {
                // Anchored on the last row, so "insert below" appends. On an empty file the anchor
                // is -1, which the menu reads as "there is no row here" and offers only the two
                // items that still mean something.
                e.preventDefault();
                setMenu({ x: e.clientX, y: e.clientY, index: rows.length - 1 });
            }}
            dockHeader={dockHeader}
            dockContent={dockContent}
            dockOverview={dockOverview}
        />
    );
}

/**
 * Right-click menu for a credits row.
 *
 * Everything here is about position, because that is what the file is. A spacer offers the same
 * placement items as any other row - putting a real row next to a blank line is how a credits block
 * gets built - but nothing to type into.
 */
function CreditsRowMenu(props: {
    x: number; y: number; index: number; spacer: boolean; lastIndex: number;
    onClose: () => void;
    onInsert: (where: 'above' | 'below' | 'end') => void;
    onInsertSpacer: (where: 'above' | 'below' | 'end') => void;
    onDelete: (index: number) => void;
    onMove: (index: number, to: number) => void;
}): React.JSX.Element {
    const run = (action: () => void) => menuAction(action, props.onClose);
    // Opened on empty space rather than on a row - which is the only way into a file with no rows
    // at all. Nothing that acts on a row belongs here.
    const onRow = props.index >= 0;

    return (
        <RowMenuFrame x={props.x} y={props.y} anchorIndex={props.index} onClose={props.onClose}>
            {onRow ? (
                <>
                    <button role="menuitem" onClick={run(() => props.onInsert('above'))}>
                        Insert row above
                    </button>
                    <button role="menuitem" onClick={run(() => props.onInsert('below'))}>
                        Insert row below
                    </button>
                    <button role="menuitem" onClick={run(() => props.onInsertSpacer('above'))}>
                        Insert blank line above
                    </button>
                    <button role="menuitem" onClick={run(() => props.onInsertSpacer('below'))}>
                        Insert blank line below
                    </button>
                </>
            ) : (
                <>
                    <button role="menuitem" onClick={run(() => props.onInsert('end'))}>
                        Add row
                    </button>
                    <button role="menuitem" onClick={run(() => props.onInsertSpacer('end'))}>
                        Add blank line
                    </button>
                </>
            )}

            {onRow && (
                <>
                    <div className="sep" />
                    <button
                        role="menuitem"
                        disabled={props.index === 0}
                        onClick={run(() => props.onMove(props.index, props.index - 1))}
                    >
                        Move up
                    </button>
                    <button
                        role="menuitem"
                        disabled={props.index >= props.lastIndex}
                        onClick={run(() => props.onMove(props.index, props.index + 1))}
                    >
                        Move down
                    </button>

                    <div className="sep" />
                    <button
                        role="menuitem"
                        className="danger"
                        onClick={run(() => props.onDelete(props.index))}
                    >
                        {props.spacer ? 'Remove blank line' : 'Delete row'}
                    </button>
                </>
            )}
        </RowMenuFrame>
    );
}

/**
 * The key cell for a credits file: a chooser over the engine's formatting directives.
 *
 * Free text here is a trap - `CENTRE`, a trailing space or a lower-case spelling all produce a row
 * the renderer does not recognise, with no feedback until the credits run. A file that already
 * contains an unrecognised directive keeps it as an extra option rather than having it silently
 * rewritten, so opening a file can never change it.
 */
function DirectiveSelect(props: {
    value: string;
    onCommit: (next: string) => void;
}): React.JSX.Element {
    const known = CREDITS_DIRECTIVES as readonly string[];
    const recognised = known.includes(props.value.trim().toUpperCase());
    const options = recognised ? known : [...known, props.value];

    return (
        <select
            className="directive"
            value={recognised ? props.value.trim().toUpperCase() : props.value}
            onChange={e => props.onCommit(e.target.value)}
        >
            {options.map(option => (
                <option key={option} value={option}>{directiveOptionText(option)}</option>
            ))}
        </select>
    );
}

function valueOf(row: LocRow, language: string): string {
    return row.values.find(v => v.language === language)?.value ?? '';
}

/**
 * Asks the host for real full screen, and carries on without it if refused.
 *
 * A webview is an iframe, and whether it is allowed to go full screen is the host's decision, not
 * ours. The panel has already maximised the editor group by the time this runs, so a refusal still
 * leaves the crawl filling the window - this only removes the remaining chrome where it is allowed.
 */
async function requestFullScreen(): Promise<void> {
    try {
        await document.documentElement.requestFullscreen?.();
    } catch {
        // Refused or unsupported; the maximised editor group is the fallback.
    }
}



// Examples from this file's own vocabulary.
const PLACEHOLDERS: Record<FilterMode, string> = { text: 'Filter rows...', wildcard: 'CENTER*', regex: '^HEADER$' };

createRoot(document.getElementById('root')!).render(<App />);
