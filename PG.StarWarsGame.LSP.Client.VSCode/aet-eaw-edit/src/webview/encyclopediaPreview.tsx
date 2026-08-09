// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The panel around the card: a centred stage with the view controls docked on the right, matching
// the layout the story graph and the localisation editors already use. Everything about how the
// popup itself looks lives in encyclopediaCard.tsx.

import { useEffect, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import styled from 'styled-components';

import { GetEncyclopediaEntryResult } from '../protocol/encyclopedia';
import { EncyclopediaCard } from './encyclopediaCard';
import { dockBodyCss, dockChromeCss, rightDockCss } from './shared/dockChrome';
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
    ${dockBodyCss}

    height: 100%;
    display: flex;
    flex-direction: row;
    min-height: 0;
    font-family: var(--vscode-font-family);
    font-size: 12px;

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

    .status-note { font-size: 11px; line-height: 1.35; }
    .empty { padding: 12px; opacity: 0.7; }
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
            };
            if (msg.type !== 'entry' || msg.entry === undefined) { return; }
            setEntry(msg.entry);
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
            ? <EncyclopediaCard entry={entry} zoom={zoom} />
            : <p className="empty">No encyclopedia entry for &apos;{entry.objectId}&apos;.</p>;

    return (
        <Shell>
            <div className="preview-stage" ref={stageRef}>{stage}</div>

            <RightDock
                initialWidth={dockWidthMemo}
                minWidth={180}
                maxWidth={420}
                onWidthChange={w => { dockWidthMemo = w; }}
                content={<>
                    <div className="dock-section">
                        <div className="dock-section-title">
                            View
                            <span className="section-count">{zoom.toFixed(2)}x</span>
                        </div>
                        <label className="field">
                            <span className="field-label">Zoom</span>
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
                        <div className="dock-hint">Scroll over the preview to zoom.</div>
                    </div>

                    <div className="dock-section">
                        <div className="dock-section-title">Body</div>
                        <label
                            className="check-field"
                            title="Preview the MP_Encyclopedia_Text body, where one is defined"
                        >
                            <input
                                type="checkbox"
                                checked={multiplayer}
                                onChange={e => onToggleMultiplayer(e.target.checked)}
                            />
                            <span>Multiplayer</span>
                        </label>
                        {multiplayer && entry !== null && entry.found && (
                            <div
                                className="status-note"
                                style={{
                                    color: entry.usedMultiplayerBody
                                        ? 'var(--vscode-descriptionForeground)'
                                        : 'var(--vscode-editorWarning-foreground, #cca700)',
                                }}
                            >
                                {entry.usedMultiplayerBody
                                    ? 'Showing MP_Encyclopedia_Text.'
                                    : 'No MP_Encyclopedia_Text on this object - showing the '
                                      + 'single-player body.'}
                            </div>
                        )}
                </div>
                </>}
            />
        </Shell>
    );
}

createRoot(document.getElementById('root')!).render(<App />);
