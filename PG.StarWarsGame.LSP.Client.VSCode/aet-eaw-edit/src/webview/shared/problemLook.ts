// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// How one preview problem is drawn.
//
// A function rather than a ternary at the call site, because the ternary that was there collapsed
// three severities into two: `severity === 'error' ? 'error' : 'warning'` put the warning triangle
// on every `info`, and a reader asking "is this something I have to fix?" got the wrong answer from
// the only thing on the row that answers it. The server emits six errors, twelve warnings and two
// infos across the preview, so the demoted case was not a rare one.

/** What the row shows for one severity: a codicon and a colour band. */
export interface ProblemLook {
    /** Codicon name, without the `codicon-` prefix. */
    icon: string;
    /** Which of the three colour bands the icon takes. */
    tone: 'error' | 'warning' | 'info';
}

const LOOKS: Record<string, ProblemLook> = {
    error: { icon: 'error', tone: 'error' },
    warning: { icon: 'warning', tone: 'warning' },
    info: { icon: 'info', tone: 'info' },
};

/**
 * The icon and tone for a problem's severity.
 *
 * `severity` crosses the wire as a plain string, so an unrecognised one is a question of which way
 * to be wrong. It is drawn as a WARNING: a finding the client does not understand is one the reader
 * should still look at, and showing it as info is how it gets skipped.
 */
export function problemLook(severity: string): ProblemLook {
    return LOOKS[severity.trim().toLowerCase()] ?? LOOKS.warning;
}

/**
 * The hardpoint to badge on a problem row, or null to badge nothing.
 *
 * Null when the message ALREADY names it, which every shipped hardpoint finding does - they are
 * written to stand alone in the Problems view. Pairing the id with such a sentence reads
 * "HP_Tartan_Cruiser_00  Hardpoint 'HP_Tartan_Cruiser_00' names no Attachment_Bone", the same
 * fourteen characters twice before a word of the finding. The badge is for the messages that do
 * not say where they are, and there it is the thing a reader scans for.
 */
export function problemWhere(
    hardpointId: string | null | undefined, message: string,
): string | null {
    const id = (hardpointId ?? '').trim();

    if (id === '' || message.toLowerCase().includes(id.toLowerCase())) {
        return null;
    }

    return id;
}

/** The small chip at the end of a problem row: what this finding IS. */
export interface ProblemTag {
    text: string;
    title: string;
    /** Whether this is a real diagnostic id, which is what decides how the chip is drawn. */
    isDiagnostic: boolean;
}

/**
 * What to badge a problem row with.
 *
 * There are two kinds of finding in this panel and they are not the same thing, which is the whole
 * reason the chip exists. A SERVER problem is a statement about the files - a hardpoint mounted on
 * a bone the hull does not have is wrong whether or not anyone opens a preview - and it carries an
 * id, which is what a suppression comment names and what a reader searches for.
 *
 * A CLIENT finding describes what the viewport could resolve at one instant. It has no file, no
 * position and is legitimately transient, so it can never be a diagnostic and has no id to show.
 * Saying "render-time" is better than leaving the chip blank: the blank reads as an id that failed
 * to arrive, and sends the reader looking for a bug in the wrong half of the product.
 */
export function problemTag(diagnosticId: string | null | undefined): ProblemTag {
    const id = (diagnosticId ?? '').trim();

    if (id === '') {
        return {
            text: 'render-time',
            title: 'Found while rendering. Not in the files, so it has no position and no id.',
            isDiagnostic: false,
        };
    }

    return {
        text: id,
        title: 'Diagnostic id. Name it in a suppression comment to silence this finding.',
        isDiagnostic: true,
    };
}
