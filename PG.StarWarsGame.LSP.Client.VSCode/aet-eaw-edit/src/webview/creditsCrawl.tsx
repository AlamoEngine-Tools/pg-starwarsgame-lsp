// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Credits crawl preview: the staged rows as a scrolling end-credit roll.
//
// It renders what is staged rather than what is on disk, so a reordered or newly inserted line is
// visible before Save. That is the point - row order and blank spacer rows are what the ordered
// model preserves, and this is the only place both are visible at once.
//
// The scroll is a single CSS transform animation rather than a per-frame JS loop: nothing to keep
// in sync, and it stays smooth over a long roll. No external assets or fonts are used.

import { useLayoutEffect, useMemo, useRef, useState } from 'react';
import styled from 'styled-components';

import { buildCrawl } from './creditsCrawlModel';
import { generateStars } from './starfield';
import { LocRow } from './loc/locRow';

const SPEEDS = [0.5, 1, 2, 4];

/**
 * Scroll rate at 1x. Pace is set in pixels per second rather than by line count, so a long file
 * scrolls at the same readable speed as a short one instead of taking proportionally longer.
 */
const PIXELS_PER_SECOND = 90;

export function CreditsCrawl(props: {
    rows: LocRow[];
    languages: string[];
    initialLanguage: string;
    onClose: () => void;
}): React.JSX.Element {
    const [language, setLanguage] = useState(props.initialLanguage);
    const [speed, setSpeed] = useState(1);
    const [running, setRunning] = useState(true);
    // Restarting remounts the roll, which is the simplest way to send it back to the bottom
    // without fighting the animation's own state.
    const [runId, setRunId] = useState(0);

    const blocks = useMemo(() => buildCrawl(props.rows, language), [props.rows, language]);
    // Dense enough to read as a sky rather than as scattered dots, and cheap: static elements the
    // browser lays out once.
    const stars = useMemo(() => generateStars(420), []);

    const stageRef = useRef<HTMLDivElement>(null);
    const rollRef = useRef<HTMLDivElement>(null);
    const [travel, setTravel] = useState({ from: 0, to: 0, seconds: 1 });

    // Measured, not expressed in percentages: translateY(100%) is a percentage of the *roll*, so a
    // tall file would start hundreds of pixels below the fold and show a black screen for the first
    // several seconds. Starting at the stage's own height puts the first line just off the bottom
    // edge whatever the file's length.
    useLayoutEffect(() => {
        const stageHeight = stageRef.current?.clientHeight ?? 0;
        const rollHeight = rollRef.current?.scrollHeight ?? 0;
        if (stageHeight === 0 || rollHeight === 0) { return; }

        const distance = stageHeight + rollHeight;
        setTravel({
            from: stageHeight,
            to: -rollHeight,
            seconds: Math.max(2, distance / (PIXELS_PER_SECOND * speed)),
        });
    }, [blocks, speed, runId]);

    return (
        <Overlay
            role="dialog"
            aria-label="Credits preview"
            tabIndex={-1}
            onKeyDown={e => { if (e.key === 'Escape') { props.onClose(); } }}
            ref={el => el?.focus()}
        >
            <div className="controls">
                <button onClick={() => setRunning(r => !r)}>{running ? 'Pause' : 'Play'}</button>
                <button onClick={() => { setRunId(id => id + 1); setRunning(true); }}>Restart</button>

                <select value={speed} onChange={e => setSpeed(Number(e.target.value))} title="Speed">
                    {SPEEDS.map(s => <option key={s} value={s}>{s}x</option>)}
                </select>

                <select value={language} onChange={e => setLanguage(e.target.value)} title="Language">
                    {props.languages.map(l => <option key={l} value={l}>{l}</option>)}
                </select>

                <span className="count">{blocks.length} lines</span>
                <button onClick={props.onClose} title="Close the preview (Esc)">Close</button>
            </div>

            <div className="stage" ref={stageRef}>
                {/* Built once and never rebuilt: a regenerated sky would twinkle as the crawl
                    advanced. */}
                <div className="starfield" aria-hidden="true">
                    {stars.map((star, i) => (
                        <span
                            key={i}
                            style={{
                                left: `${star.x}%`,
                                top: `${star.y}%`,
                                width: star.size,
                                height: star.size,
                                opacity: star.opacity,
                            }}
                        />
                    ))}
                </div>

                {blocks.length === 0
                    ? <p className="empty">Nothing to show - this file has no text in {language}.</p>
                    : (
                        <div
                            key={runId}
                            className="roll"
                            ref={rollRef}
                            style={{
                                animationDuration: `${travel.seconds}s`,
                                animationPlayState: running ? 'running' : 'paused',
                                ['--crawl-from' as string]: `${travel.from}px`,
                                ['--crawl-to' as string]: `${travel.to}px`,
                            }}
                        >
                            {blocks.map((block, i) => (block.kind === 'gap'
                                ? <div key={i} className="gap" />
                                : (
                                    <div
                                        key={i}
                                        className={[
                                            block.style === 'header' ? 'header' : 'line',
                                            block.untranslated ? 'missing' : '',
                                        ].filter(Boolean).join(' ')}
                                    >
                                        {block.text}
                                    </div>
                                )))}
                        </div>
                    )}
            </div>
        </Overlay>
    );
}

const Overlay = styled.div`
    position: absolute;
    inset: 0;
    z-index: 10;
    display: flex;
    flex-direction: column;
    background: #000;
    color: #fff;
    outline: none;

    .controls {
        display: flex;
        align-items: center;
        gap: 6px;
        padding: 6px 8px;
        background: var(--vscode-editorWidget-background, #252526);
        color: var(--vscode-foreground, #ccc);
        border-bottom: 1px solid var(--vscode-panel-border, #444);
        flex-shrink: 0;
    }

    .controls button, .controls select {
        background: var(--vscode-button-secondaryBackground, #3a3d41);
        color: var(--vscode-button-secondaryForeground, #ccc);
        border: none;
        padding: 3px 10px;
        font: inherit;
        cursor: pointer;
    }

    .count { margin-left: auto; opacity: 0.7; }

    /* The sky is drawn as elements rather than tiled gradients - see starfield.ts for why. Static,
       because the crawl is what moves. */
    .stage {
        flex: 1;
        overflow: hidden;
        position: relative;
        background-color: #000;
    }

    .starfield {
        position: absolute;
        inset: 0;
        pointer-events: none;
    }

    .starfield span {
        position: absolute;
        border-radius: 50%;
        background: #fff;
    }

    .empty { text-align: center; margin-top: 2em; opacity: 0.7; }

    .roll {
        position: absolute;
        left: 0;
        right: 0;
        text-align: center;
        /* A humanist sans in the spirit of the films' credits, from fonts already on the system -
           the real face is licensed and cannot be bundled. */
        font-family: 'Franklin Gothic Medium', 'Trebuchet MS', 'Segoe UI', Arial, sans-serif;
        color: #4dc3ec;
        font-size: 20px;
        line-height: 1.55;
        letter-spacing: 0.02em;
        animation-name: crawl;
        animation-timing-function: linear;
        animation-iteration-count: 1;
        animation-fill-mode: forwards;
        will-change: transform;
    }

    /* The two directives the engine uses. HEADER is the label - a role or a section title - set
       smaller and lighter; CENTER is the name beneath it, larger and bold in caps. That is the
       relationship the films' credits use and the game follows. */
    .header {
        padding: 0.4em 2em 0;
        font-size: 0.8em;
        font-weight: 500;
        opacity: 0.92;
    }

    .line {
        padding: 0 2em;
        font-size: 1.25em;
        font-weight: 700;
        text-transform: uppercase;
    }

    /* A row with no text in this language: shown as its key so the gap is visible, dimmed and in
       normal case so it cannot be mistaken for real credit text. */
    .missing {
        opacity: 0.4;
        font-style: italic;
        font-weight: 400;
        text-transform: none;
    }

    .gap { height: 1.4em; }

    /* Pixel distances measured from the stage and the roll - see the layout effect. Percentages
       here would be relative to the roll's own height, which is what made a long file sit off
       screen for the first several seconds. */
    @keyframes crawl {
        from { transform: translateY(var(--crawl-from, 100%)); }
        to { transform: translateY(var(--crawl-to, -100%)); }
    }

    /* Someone who has asked the system for less motion gets a readable, scrollable list instead of
       a moving one. */
    @media (prefers-reduced-motion: reduce) {
        .stage { overflow: auto; }
        .roll { position: static; animation: none; padding: 1em 0; }
    }
`;
