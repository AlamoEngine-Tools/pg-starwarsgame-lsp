// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Making the mid-LOD labels say something.
//
// Between K_LABEL and K_DETAIL the overview draws each event as a rectangle with its name inside.
// A node there is only ~45px across, and the old code guessed each character was 0.55em wide and
// hard-cut the name to whatever that arithmetic allowed - so most labels were sliced to their
// first few characters.
//
// Nothing here infers meaning from how events are NAMED. Shared prefixes like `Story_` are a
// convention, not a rule, and a mod that names things differently would be penalised by any
// cleverness built on top of one.

/**
 * The longest leading run of `text` that fits within `maxWidth`.
 *
 * No ellipsis: at this zoom every character is worth more than a marker saying characters are
 * missing, and the view is plainly schematic already.
 */
export function fitLabel(text: string, maxWidth: number, measure: (s: string) => number): string {
    if (maxWidth <= 0) { return ''; }
    if (measure(text) <= maxWidth) { return text; }

    // Proportional from the average advance, then walk back until it really fits - one or two
    // steps in practice, and no per-character measuring.
    let end = Math.min(text.length, Math.max(0, Math.floor(maxWidth / (measure(text) / text.length))));
    while (end > 0 && measure(text.slice(0, end)) > maxWidth) { end--; }
    return text.slice(0, end);
}

/** Never shrink below this: smaller is not a label, it is texture. */
export const MIN_LABEL_PX = 7;

/** Line spacing as a multiple of the font size. */
export const LINE_RATIO = 1.15;

/**
 * How many lines of `fontPx` text fit in `height`.
 *
 * Never fewer than one: a node too short for a line still says its name, and a label that vanished
 * would be worse than one that reaches its edges.
 */
export function linesThatFit(height: number, fontPx: number): number {
    return Math.max(1, Math.floor(height / (fontPx * LINE_RATIO)));
}

/**
 * Per-box label sizing for one frame.
 *
 * The size used to be chosen once per frame, from the graph's longest label against its LARGEST
 * node. Every shorter node inherited a line budget its box could not hold, and since the block is
 * centred it spilled out of both ends into the rows above and below. Capping each node's budget
 * fixes the collision but truncates those labels; sizing per box keeps the whole name, because a
 * smaller font buys more characters per line AND more lines at once.
 *
 * Still measured against `longest` rather than each node's own label, so two boxes of the same size
 * always render at the same size - the variation the reader sees tracks the node's shape, not its
 * name. Memoised because a campaign draws hundreds of nodes from a handful of box sizes, and
 * `labelLayout` walks down from the maximum size to find its answer.
 *
 * One sizer per frame: the boxes are in SCREEN pixels, so every size changes with the zoom.
 */
export function createLabelSizer(
    longest: string, advancePerPx: number, maxPx: number,
): (width: number, height: number) => { fontPx: number; maxLines: number } {
    const cache = new Map<string, { fontPx: number; maxLines: number }>();
    return (width, height) => {
        const key = `${Math.round(width)}x${Math.round(height)}`;
        let fit = cache.get(key);
        if (fit === undefined) {
            fit = labelLayout(longest, width, height, advancePerPx, maxPx);
            cache.set(key, fit);
        }
        return fit;
    };
}

/**
 * The font size and line budget at which `longest` fits a `width` x `height` node.
 *
 * Size and line count cannot be chosen separately - a smaller font fits more characters per line
 * AND more lines - so this walks down from `maxPx` and takes the first size at which the label
 * really wraps into the available lines. The test runs the actual wrap rather than dividing width
 * by character count: breaking at separators leaves ragged line ends, so a grid that looks big
 * enough on paper still drops the last segment. If no size fits, it settles at MIN_LABEL_PX and
 * the caller truncates; below that the text is texture, not a label.
 *
 * `longest` comes from the whole graph rather than what is on screen, so text does not resize while
 * panning. `advancePerPx` is the average character advance per pixel of font size, measured once
 * per frame from the real font - the old code assumed 0.55 and cut labels in the wrong place.
 */
export function labelLayout(
    longest: string, width: number, height: number, advancePerPx: number, maxPx: number,
): { fontPx: number; maxLines: number } {
    const linesAt = (px: number): number => linesThatFit(height, px);

    if (longest.length === 0 || advancePerPx <= 0 || !Number.isFinite(width)
        || !Number.isFinite(height)) {
        return { fontPx: maxPx, maxLines: linesAt(maxPx) };
    }

    const floor = Math.min(MIN_LABEL_PX, maxPx);
    for (let px = Math.max(floor, Math.floor(maxPx)); px > floor; px--) {
        const maxLines = linesAt(px);
        // One line of slack in the budget: coming back under it means nothing was dropped.
        const wrapped = wrapLabel(longest, width, maxLines + 1,
            s => s.length * advancePerPx * px);
        if (wrapped.length > 0 && wrapped.length <= maxLines) { return { fontPx: px, maxLines }; }
    }
    return { fontPx: floor, maxLines: linesAt(floor) };
}

/**
 * Breaks `text` into at most `maxLines` lines that each fit `maxWidth`.
 *
 * One line is not enough at this zoom: a node is ~63px across, which is about fifteen characters
 * even at the smallest legible size, while event names routinely run to thirty-five. Shrinking
 * further just makes an unreadable smudge, so the remaining room is vertical - a node is several
 * lines tall.
 *
 * Breaks are preferred at `_`, `.`, `-` and spaces, which is line breaking, not interpretation: no
 * meaning is read into the segments and nothing is discarded for looking like a convention. A
 * segment too long for a line on its own is split mid-word rather than overflowing.
 */
export function wrapLabel(
    text: string, maxWidth: number, maxLines: number, measure: (s: string) => number,
): string[] {
    if (maxWidth <= 0 || maxLines <= 0) { return []; }

    // Keep each separator attached to the chunk it follows, so a break reads as a word boundary.
    const chunks = text.split(/(?<=[_.\- ])/);
    const lines: string[] = [];
    let current = '';

    for (const chunk of chunks) {
        if (current !== '' && measure(current + chunk) > maxWidth) {
            lines.push(current);
            if (lines.length === maxLines) { return lines; }
            current = '';
        }
        // Still too wide alone: hard-split it across as many lines as it needs.
        let rest = chunk;
        while (measure(current + rest) > maxWidth && current === '') {
            const head = fitLabel(rest, maxWidth, measure);
            if (head === '') { break; }
            if (head === rest) { break; }
            lines.push(head);
            if (lines.length === maxLines) { return lines; }
            rest = rest.slice(head.length);
        }
        current += rest;
    }

    if (current !== '') { lines.push(current); }
    return lines.slice(0, maxLines);
}
