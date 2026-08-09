// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The panel around the card: zoom, the SP/MP toggle, and the message channel to the extension.
// Everything about how the popup itself looks lives in encyclopediaCard.tsx.

import { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';

import { GetEncyclopediaEntryResult } from '../protocol/encyclopedia';
import { EncyclopediaCard } from './encyclopediaCard';

declare function acquireVsCodeApi(): { postMessage(message: unknown): void };
const vscode = acquireVsCodeApi();

/**
 * Zoom presets.
 *
 * Wrap points depend only on the width-to-font-size ratio, so scaling both by the same factor
 * leaves the line breaks identical - the card is legible at 3x while still breaking exactly where
 * the game breaks. 1x is the game's own size, which is genuinely tiny.
 */
const ZOOM_STEPS = [1, 2, 3, 4, 6];

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

    if (entry === null) {
        return <p style={{ padding: 12, opacity: 0.7 }}>Waiting for the editor...</p>;
    }

    if (!entry.found) {
        return (
            <p style={{ padding: 12, opacity: 0.7 }}>
                No encyclopedia entry for &apos;{entry.objectId}&apos;.
            </p>
        );
    }

    const onToggleMultiplayer = (next: boolean): void => {
        setMultiplayer(next);
        vscode.postMessage({ type: 'setMultiplayer', multiplayer: next });
    };

    return (
        <div style={{ padding: 12, fontFamily: 'var(--vscode-font-family)', fontSize: 12 }}>
            <div style={{ display: 'flex', gap: 12, alignItems: 'center', marginBottom: 10 }}>
                <label>
                    Zoom{' '}
                    <select value={zoom} onChange={e => setZoom(Number(e.target.value))}>
                        {ZOOM_STEPS.map(z => <option key={z} value={z}>{z}x</option>)}
                    </select>
                </label>
                <label title="Preview the MP_Encyclopedia_Text body, where one is defined">
                    <input
                        type="checkbox"
                        checked={multiplayer}
                        onChange={e => onToggleMultiplayer(e.target.checked)}
                    />{' '}
                    Multiplayer
                </label>
                {multiplayer && (
                    <span
                        style={{
                            color: entry.usedMultiplayerBody
                                ? 'var(--vscode-descriptionForeground)'
                                : 'var(--vscode-editorWarning-foreground, #cca700)',
                        }}
                    >
                        {entry.usedMultiplayerBody
                            ? 'showing MP_Encyclopedia_Text'
                            : 'no MP_Encyclopedia_Text on this object - showing the '
                              + 'single-player body'}
                    </span>
                )}
            </div>

            <EncyclopediaCard entry={entry} zoom={zoom} />
        </div>
    );
}

createRoot(document.getElementById('root')!).render(<App />);
