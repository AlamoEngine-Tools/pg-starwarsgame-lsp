// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

/**
 * Wraps one encyclopedia line the way the engine does: on a CHARACTER COUNT, not by measuring text.
 *
 * The budget is the text component's own `Size` X - stock `encyclopedia_text` is 41, and EaWX
 * widened theirs to 56. No font is involved, which is why a mod can author a run of `=` that spans
 * the card exactly and rely on it never breaking: they counted characters, and so does the game.
 *
 * This replaced a calibrated pixel measurement that wrapped a tuned divider two lines early. That
 * measurement was fitted to reproduce the shipped wrap of Luke Skywalker's biography, and it is
 * worth saying plainly why it could appear to work: a character budget and a proportional width
 * agree often enough on ordinary prose to look right, and disagree completely on a run of
 * identical glyphs. Do not reintroduce font metrics here.
 *
 * The card's visible width - 262 stock, 340 in EaWX - is backdrop geometry and has nothing to do
 * with where lines break.
 */
export function wrapByCharacterBudget(text: string, budget: number): string[] {
    if (text.length === 0) {
        return [];
    }
    // A component that declares no usable Size X gives no budget to wrap against. Taking it
    // literally would compare every length against zero and emit one character per line.
    if (!Number.isFinite(budget) || budget < 2) {
        return text.split('\n');
    }

    const out: string[] = [];
    for (const paragraph of text.split('\n')) {
        wrapOne(paragraph, budget, out);
    }
    return out;
}

/**
 * Greedy, one character at a time, exactly as the engine accumulates it.
 *
 * The cut fires when the line REACHES the budget rather than exceeds it, so the longest line a
 * budget of 41 can produce is 40 characters. That is not a guess: the shipped second line of Luke's
 * biography is exactly 40, sitting on the boundary.
 */
function wrapOne(paragraph: string, budget: number, out: string[]): void {
    if (paragraph.length === 0) {
        out.push('');
        return;
    }

    let line = '';
    // Where the last space sits within `line`, or -1 when this run has none to cut at.
    let lastSpace = -1;

    for (let i = 0; i < paragraph.length; i++) {
        const ch = paragraph[i];
        line += ch;
        if (ch === ' ') {
            lastSpace = line.length - 1;
        }

        if (line.length < budget) {
            continue;
        }

        if (lastSpace < 0) {
            // No space in the whole run - a divider, a long identifier - so the line is chopped
            // where it ran out and the character that overflowed starts the next one.
            out.push(line.slice(0, -1));
            line = ch;
        } else {
            // Cut at the space, which is consumed rather than carried to the next line.
            out.push(line.slice(0, lastSpace));
            line = line.slice(lastSpace + 1);
            lastSpace = line.lastIndexOf(' ');
        }
    }

    if (line.length > 0) {
        out.push(line);
    }
}
