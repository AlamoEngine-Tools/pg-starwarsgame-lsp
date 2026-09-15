// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import type { SetLocalisationProjectFormatResult } from './protocol/localisation';

/**
 * The choices and the outcome wording for "Set Localisation Project Format" (#121), kept free of the
 * VS Code API so the command, the navigator button that runs it, and the tests all read one thing.
 */

export interface FormatChoice {
    /** Sent to the server as-is. */
    format: string;
    label: string;
    description: string;
    /** Whether the project already declares this format. */
    current: boolean;
}

export interface FormatOutcome {
    kind: 'info' | 'warning' | 'error';
    text: string;
}

/**
 * Every format a `.pgproj` may declare. DAT is here although the conversion command leaves it out:
 * converting writes files, this only changes which ones the project loads, and switching to the DAT
 * files a project already has is what #121 asked for.
 */
const FORMATS: readonly { format: string; description: string }[] = [
    { format: 'DAT', description: 'The game\'s binary files (.dat), one per language' },
    { format: 'CSV', description: 'Comma-separated values (.csv)' },
    { format: 'XML', description: 'eaw-translation v1 XML (.xml)' },
    { format: 'NLS', description: 'Java-style properties (.properties), one per language' },
];

export function formatChoices(current: string | null): FormatChoice[] {
    const declared = current?.toUpperCase() ?? null;
    return FORMATS.map(({ format, description }) => ({
        format,
        label: format,
        description: format === declared ? `Current - ${description}` : description,
        current: format === declared,
    }));
}

export function formatOutcome(result: SetLocalisationProjectFormatResult): FormatOutcome {
    if (result.error) {
        return { kind: 'error', text: `EaWEdit: ${result.error}` };
    }

    const format = result.format ?? '';
    if (!result.changed) {
        return { kind: 'info', text: `EaWEdit: The project already loads ${format}.` };
    }

    const from = result.previousFormat ? ` instead of ${result.previousFormat.toUpperCase()}` : '';
    if (result.filesInFormat === 0) {
        return {
            kind: 'warning',
            text: `EaWEdit: The project now loads ${format}${from}, but there are no ${format} files in its `
                + 'localisation directory, so no text is loaded. No files were converted.',
        };
    }

    const files = result.filesInFormat === 1 ? '1 file' : `${result.filesInFormat} files`;
    return {
        kind: 'info',
        text: `EaWEdit: The project now loads ${format}${from}: ${files}. No files were converted.`,
    };
}
