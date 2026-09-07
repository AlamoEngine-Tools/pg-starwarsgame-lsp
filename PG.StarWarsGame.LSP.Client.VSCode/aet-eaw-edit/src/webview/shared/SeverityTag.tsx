// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The verdict on what is open, at the right-hand end of a dock header.
//
// Four panels draw this - the preview, the story graph, the encyclopedia card and the localisation
// grid - and all four wrote the same three things out by hand: the class string
// `icon-btn validate-btn header-right sev-${severity}` character for character, a codicon chosen
// from the severity, and a count rendered only when non-zero with a leading space. Those three are
// what is shared here, because those three were already identical.
//
// What was NOT identical stays a prop, and deliberately so. Pressing it runs a check in the story
// graph and opens a panel in the other three; two panels disable it when there is nothing to report
// and two leave it live. Giving them one component is not a reason to give them one behaviour.
//
// It is a sibling of IconButton rather than a variant of it. Its glyph is a severity mark, and a
// severity mark is the one thing this extension still draws with a codicon, so that it matches the
// marks the rest of the editor shows; IconButton draws Tabler icons from a named catalogue. Its
// colour also comes from state rather than from a variant the caller composes. Folding either of
// those into the most-used primitive in the app would put a branch through every button in it.
//
// A tag, not a button, in three of the four cases: it reports a verdict and opens the report. The
// class it carries is still called `validate-btn`, which is the older name for the same control.

import { ValidationState } from '../loc/useLocPanel';
import { Enablement } from './Button';

/** The codicon that stands for a verdict. */
export function severityIcon(severity: ValidationState): string {
    return severity === 'unvalidated' ? 'question'
        : severity === 'error' ? 'error'
            : severity === 'warning' ? 'warning'
                : severity === 'info' ? 'info' : 'check';
}

export type SeverityTagProps = Enablement & {
    severity: ValidationState;

    /** How many things there are to report. Drawn beside the glyph, and hidden when it is zero. */
    count: number;

    /** The verdict, and what pressing it will do. */
    title: string;

    /** Whether the report this opens is open. Left out where pressing runs a check instead. */
    expanded?: boolean;

    onClick: (event: React.MouseEvent<HTMLButtonElement>) => void;
};

export function SeverityTag({
    severity, count, title, expanded, disabled, disabledReason, onClick,
}: SeverityTagProps): React.JSX.Element {
    return (
        <button
            type="button"
            className={`icon-btn validate-btn header-right sev-${severity}`}
            title={disabled ? disabledReason : title}
            aria-expanded={expanded}
            disabled={disabled}
            onClick={onClick}
        >
            <span className={`codicon codicon-${severityIcon(severity)}`} />
            {/* Nothing rather than a bare "0": a zero here reads as a value someone should look at,
                where what is meant is that there is nothing to look at. */}
            {count > 0 ? ` ${count}` : ''}
        </button>
    );
}
