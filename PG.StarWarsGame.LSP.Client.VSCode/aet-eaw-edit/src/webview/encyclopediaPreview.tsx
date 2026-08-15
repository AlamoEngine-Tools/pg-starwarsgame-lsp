// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The panel around the card: a centred stage with the tool dock on the right, matching the layout
// the story graph and the localisation editors already use. Everything about how the popup itself
// looks lives in encyclopediaCard.tsx.
//
// The dock follows the extension's three levels, which are about WHAT a control is, not where it
// happened to be written: the header reports state (here, the severity tag), the content is the
// editor's library (here, the object's pool of ship names), and the foot holds the view controls
// (zoom, which faction frame, which body). Notices used to be five coloured paragraphs scattered
// beside whichever control raised them; they now all report through the tag.
//
// Pressing the tag opens a bar across the foot of the EDITOR, not a list inside the dock - the same
// place the story graph and the localisation grids put theirs, via the shared ProblemsPanel. The
// dock is the wrong home for them and LocProblemsBar already records why: it is narrow, so messages
// are truncated, and a list that appears and disappears there shoves everything around it.

import { useEffect, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import styled from 'styled-components';

import { GetEncyclopediaEntryResult } from '../protocol/encyclopedia';
import { EncyclopediaCard } from './encyclopediaCard';
import { encyclopediaNotices, noticeSeverity } from './encyclopediaNotices';
import { severityIconFor } from './loc/validateState';
import {
    dockBodyCss, dockChromeCss, dockHeaderCss, dockOverviewCss, problemsPanelCss, rightDockCss,
} from './shared/dockChrome';
import { ProblemsPanel } from './shared/ProblemsPanel';
import { RightDock } from './shared/RightDock';

declare function acquireVsCodeApi(): { postMessage(message: unknown): void };
const vscode = acquireVsCodeApi();

/**
 * Zoom range.
 *
 * Wrap points depend only on the width-to-font-size ratio, so scaling both by the same factor
 * leaves the line breaks identical - the card is legible at 3x while still breaking exactly where
 * the game breaks. 1x is the game's own size, which is genuinely tiny.
 */
const ZOOM_MIN = 1;
const ZOOM_MAX = 5;
const ZOOM_STEP = 0.25;

const clampZoom = (z: number): number =>
    Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, Math.round(z / ZOOM_STEP) * ZOOM_STEP));

// Module-level so a remount - the webview reloads when its tab is restored - keeps the dock width,
// the same way the localisation grid does.
let dockWidthMemo = 220;

const Shell = styled.div`
    ${dockChromeCss}
    ${rightDockCss}
    ${dockHeaderCss}
    ${dockBodyCss}
    ${dockOverviewCss}
    ${problemsPanelCss}

    height: 100%;
    display: flex;
    flex-direction: row;
    min-height: 0;
    font-family: var(--vscode-font-family);
    font-size: 12px;

    /* The stage column: the card, with the notes bar under it. The bar sits INSIDE this column so
       it borders the dock rather than running underneath it - the dock stays full height. Same
       arrangement as the localisation editors (.grid-column) and the story graph (.canvas-column).
       min-height: 0 is load-bearing - without it the scrolling stage refuses to shrink below its
       content and pushes the bar off the bottom. */
    .stage-column { flex: 1; display: flex; flex-direction: column; min-width: 0; min-height: 0; }

    /* The card sits in the middle of whatever space is left, both ways, and the stage scrolls
       rather than the page when a tall card or a high zoom outgrows it.

       'safe' centring, not plain: once the card is wider than the stage - easy at 4x or 6x, or in
       a narrow panel - plain centring pushes the overflow equally past both edges, and the part
       past the START edge cannot be scrolled back to. 'safe' falls back to flex-start exactly in
       that case, so the whole card stays reachable. */
    .preview-stage {
        flex: 1;
        min-width: 0;
        min-height: 0;
        overflow: auto;
        display: flex;
        align-items: safe center;
        justify-content: safe center;
        padding: 16px;
    }
    .preview-stage > * { flex: 0 0 auto; }

    .empty { padding: 12px; opacity: 0.7; }

    /* ── Header: the severity tag ───────────────────────────────────────── */
    .dock-header { min-height: 34px; }
    .header-title {
        font-weight: 600;
        text-align: center;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        /* Clear of the tag floating over the right edge. */
        padding: 0 34px;
    }

    /* ── Foot of the editor: what the tag has to say ────────────────────── */
    .encyclopedia-problems {
        position: relative;
        flex-shrink: 0;
        display: flex;
        flex-direction: column;
        min-height: 0;
        border-top: 1px solid var(--vscode-panel-border, #444);
        background: var(--vscode-sideBar-background, #252526);
        font-size: 12px;
    }
    /* This editor's notices are PROSE, not the short entry labels the other two report, so they
       wrap instead of being clamped to the row - the shared rule's one-line ellipsis would hide
       most of every message. Full width is available here, which is the whole reason these moved
       out of the dock. */
    .encyclopedia-problems .problem-row { align-items: flex-start; padding: 3px 6px; }
    .encyclopedia-problems .problem-msg {
        white-space: normal;
        overflow: visible;
        line-height: 1.35;
        /* A Windows path has no break opportunity in it, so without this it runs off the edge. */
        overflow-wrap: anywhere;
    }
    .encyclopedia-problems .problem-row .codicon { margin-top: 2px; }

    /* ── Content: the ship name pool ────────────────────────────────────── */
    /* A real list, one name per row, scrolling in place - the Star Destroyer pool alone is nineteen
       entries and the dock must not grow to fit them. Deliberately NOT badges: these are not
       pressable, and a chip with a hover state promises a click that does nothing. The panel draws
       the name; which one it drew is reported, not chosen. */
    /* The library fills the level it owns rather than stopping at a guessed height: a nineteen-name
       pool should use a tall dock, and a short one should not leave a box of dead space. */
    .dock-content { display: flex; flex-direction: column; }
    .dock-content > .dock-section { flex: 1; min-height: 0; }

    .name-list {
        display: flex;
        flex-direction: column;
        margin: 0;
        padding: 2px;
        list-style: none;
        flex: 1;
        min-height: 60px;
        overflow-y: auto;
        border: 1px solid var(--vscode-panel-border, #444);
        border-radius: 3px;
        background: var(--vscode-editorWidget-background, rgba(128, 128, 128, 0.06));
    }
    .name-list li {
        display: flex;
        align-items: baseline;
        gap: 6px;
        padding: 3px 6px;
        border-left: 2px solid transparent;
        cursor: default;
    }
    /* The one currently on the card, called out in words as well as weight - the marker has to say
       what it means, and an outline alone reads as "selected", which would imply a choice. */
    .name-list li.drawn {
        border-left-color: var(--vscode-focusBorder, #007fd4);
        background: var(--vscode-list-inactiveSelectionBackground, rgba(128, 128, 128, 0.14));
        font-weight: 600;
    }
    .name-list li .drawn-tag {
        margin-left: auto;
        font-size: 10px;
        font-weight: normal;
        opacity: 0.7;
        white-space: nowrap;
    }
    /* ── Foot: the view controls ────────────────────────────────────────── */
    .dock-overview .field + .field { margin-top: 2px; }
`;

function App(): React.JSX.Element {
    const [entry, setEntry] = useState<GetEncyclopediaEntryResult | null>(null);
    const [zoom, setZoom] = useState(3);
    /**
     * What the user asked to see - NOT what the server was able to give.
     *
     * Those are two different facts and merging them broke the toggle: driving this from the
     * response's `usedMultiplayerBody` meant that ticking the box on a unit with no MP text
     * immediately un-ticked it again, because the server correctly answered "no MP text, here is
     * the single-player body". The toggle looked inert and the explanation flashed past. The
     * request stays here; whether it was honoured is read off the entry.
     */
    const [multiplayer, setMultiplayer] = useState(false);
    /**
     * Which faction's frame to draw. The engine takes this from the viewing player's faction; a
     * preview has no player, so the user picks. Held here and never driven from the response - the
     * same rule the multiplayer toggle had to learn.
     */
    const [factionSlot, setFactionSlot] = useState(0);
    /**
     * The ship name the panel drew for this object, or null when it has no pool. Held by the PANEL,
     * not here, so it survives a webview reload and is only re-drawn when the panel itself is
     * closed and reopened.
     */
    const [shipName, setShipName] = useState<string | null>(null);
    /**
     * Whether the notice list is unfolded. Starts closed: the tag already reports how many and how
     * bad, which is all most cards need, and a dock that opens with a paragraph of caveats over its
     * content buries the thing the user came to look at.
     */
    const [noticesOpen, setNoticesOpen] = useState(false);
    const stageRef = useRef<HTMLDivElement>(null);

    /**
     * Wheel over the stage zooms.
     *
     * Registered by hand rather than with React's `onWheel` because the listener has to be
     * non-passive: React adds wheel handlers passively, and a passive listener cannot call
     * preventDefault, so every notch would zoom AND scroll the stage underneath it.
     */
    useEffect(() => {
        const stage = stageRef.current;
        if (stage === null) { return; }

        const onWheel = (e: WheelEvent): void => {
            e.preventDefault();
            setZoom(z => clampZoom(z + (e.deltaY < 0 ? ZOOM_STEP : -ZOOM_STEP)));
        };

        stage.addEventListener('wheel', onWheel, { passive: false });
        return () => stage.removeEventListener('wheel', onWheel);
    }, []);

    useEffect(() => {
        const handle = (event: MessageEvent): void => {
            const msg = event.data as {
                type: string;
                entry?: GetEncyclopediaEntryResult;
                multiplayer?: boolean;
                shipName?: string | null;
            };
            if (msg.type !== 'entry' || msg.entry === undefined) { return; }
            setEntry(msg.entry);
            setShipName(msg.shipName ?? null);
            // The mode the host actually requested. Note this is not `usedMultiplayerBody` - it is
            // what was asked for, which is what the toggle represents.
            if (typeof msg.multiplayer === 'boolean') { setMultiplayer(msg.multiplayer); }
        };

        window.addEventListener('message', handle);
        vscode.postMessage({ type: 'ready' });
        return () => window.removeEventListener('message', handle);
    }, []);

    const onToggleMultiplayer = (next: boolean): void => {
        setMultiplayer(next);
        vscode.postMessage({ type: 'setMultiplayer', multiplayer: next });
    };

    const stage = entry === null
        ? <p className="empty">Waiting for the editor...</p>
        : entry.found
            ? (
                <EncyclopediaCard
                    entry={shipName === null ? entry : { ...entry, unitClass: shipName }}
                    zoom={zoom}
                    factionSlot={factionSlot}
                />
            )
            : <p className="empty">No encyclopedia entry for &apos;{entry.objectId}&apos;.</p>;

    // One entry per slot encyclopedia_back declares. Offered only when there is a choice to make -
    // a mod that ships a single frame gets no picker rather than a control with one option.
    const factionFrames = entry?.chrome?.factionFrames ?? [];

    // Everything the preview has to report about this card, in one place - see encyclopediaNotices.
    const notices = encyclopediaNotices(entry, { multiplayer, factionSlot });
    const severity = noticeSeverity(notices);
    const noticeWord = notices.length === 1 ? 'note' : 'notes';
    // Counted heading, the shape the other two editors' bars use. The warning count is only worth
    // showing when it is not simply the total - "2 notes, 2 warnings" says the same thing twice.
    const warnings = notices.filter(n => n.severity === 'warning').length;
    const noticeTitle = `Notes (${notices.length}${
        warnings > 0 && warnings < notices.length
            ? `, ${warnings} warning${warnings === 1 ? '' : 's'}`
            : ''})`;
    const title = entry === null
        ? 'Encyclopedia'
        : entry.displayName?.trim() || entry.objectId;

    return (
        <Shell>
            <div className="stage-column">
                <div className="preview-stage" ref={stageRef}>{stage}</div>

                {noticesOpen && notices.length > 0 && (
                    <ProblemsPanel
                        className="encyclopedia-problems"
                        memoKey="encyclopedia"
                        defaultHeight={120}
                        title={noticeTitle}
                        onClose={() => setNoticesOpen(false)}
                    >
                        <div className="problem-list">
                            {notices.map(notice => (
                                <div key={notice.message} className="problem-row">
                                    <span
                                        className={'codicon sev-' + notice.severity + ' codicon-'
                                            + (notice.severity === 'warning' ? 'warning' : 'info')}
                                        aria-hidden="true"
                                    />
                                    <span className="problem-msg">{notice.message}</span>
                                </div>
                            ))}
                        </div>
                    </ProblemsPanel>
                )}
            </div>

            <RightDock
                initialWidth={dockWidthMemo}
                minWidth={180}
                maxWidth={420}
                onWidthChange={w => { dockWidthMemo = w; }}
                header={<>
                    <span className="header-title" title={entry?.objectId}>{title}</span>
                    <button
                        className={`icon-btn validate-btn header-right sev-${severity}`}
                        disabled={notices.length === 0}
                        aria-expanded={noticesOpen}
                        onClick={() => setNoticesOpen(open => !open)}
                        title={notices.length === 0
                            ? 'Nothing to report about this card.'
                            : `${notices.length} ${noticeWord} about this card. `
                              + `Press to ${noticesOpen ? 'hide' : 'read'} them.`}
                    >
                        <span className={`codicon codicon-${severityIconFor(severity)}`} />
                        {notices.length > 0 ? ` ${notices.length}` : ''}
                    </button>
                </>}
                content={<>
                    <div className="dock-section">
                        <div className="dock-section-title">
                            Ship names
                            {entry?.shipNames && (
                                <span className="section-count">
                                    {entry.shipNames.names.length}
                                </span>
                            )}
                        </div>
                        {/* Three cases, and the middle one is why this is not a ternary: a pool
                            that read empty must not draw an empty bordered box, and must not be
                            described as "not registered" either - it IS wired up, and the notes
                            bar carries the reason it came back empty. */}
                        {entry?.shipNames && entry.shipNames.names.length > 0 && (
                            <ul className="name-list">
                                {entry.shipNames.names.map(name => (
                                    <li
                                        key={name}
                                        className={name === shipName ? 'drawn' : undefined}
                                    >
                                        <span>{name}</span>
                                        {name === shipName && (
                                            <span className="drawn-tag">on the card</span>
                                        )}
                                    </li>
                                ))}
                            </ul>
                        )}
                        {entry?.shipNames && entry.shipNames.names.length === 0 && (
                            <div className="dock-hint">
                                This object is wired up for individual names, but none could be
                                read. See the notes for why.
                            </div>
                        )}
                        {entry !== null && !entry.shipNames && (
                            <div className="dock-hint">
                                This object shows its class line. Only objects registered in
                                GameConstants under ShipNameTextFiles draw an individual name here.
                            </div>
                        )}
                    </div>
                </>}
                overview={<>
                    <label className="field">
                        <span className="field-label">Zoom {zoom.toFixed(2)}x</span>
                        <input
                            type="range"
                            min={ZOOM_MIN}
                            max={ZOOM_MAX}
                            step={ZOOM_STEP}
                            value={zoom}
                            onChange={e => setZoom(clampZoom(Number(e.target.value)))}
                            title="Drag, or scroll over the preview"
                        />
                    </label>

                    {factionFrames.length > 1 && (
                        <label className="field">
                            <span className="field-label">Faction frame</span>
                            <select
                                value={factionSlot}
                                onChange={e => setFactionSlot(Number(e.target.value))}
                                title="Which faction's frame the card is drawn with. The game picks
                                    this from the viewing player's faction."
                            >
                                {factionFrames.map(frame => (
                                    <option
                                        key={frame.slot}
                                        value={frame.slot}
                                        title={frame.textureName}
                                    >
                                        {frame.slotName ?? `Slot ${frame.slot}`}
                                    </option>
                                ))}
                            </select>
                        </label>
                    )}

                    <label
                        className="check-field"
                        title="Preview the MP_Encyclopedia_Text body, where one is defined"
                    >
                        <input
                            type="checkbox"
                            checked={multiplayer}
                            onChange={e => onToggleMultiplayer(e.target.checked)}
                        />
                        <span>Multiplayer body</span>
                    </label>
                </>}
            />
        </Shell>
    );
}

createRoot(document.getElementById('root')!).render(<App />);
