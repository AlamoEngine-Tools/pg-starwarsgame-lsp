// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// How the Validate control describes itself.
//
// The file is checked as soon as it opens, so this reports something true from the start rather
// than sitting inert until it is pressed. It is always pressable: a file can have problems with
// nothing staged at all - a duplicate key it was saved with - and re-checking is always a
// reasonable thing to ask for.

import { ValidationState } from './useLocPanel';

export function severityIconFor(validation: ValidationState): string {
    return validation === 'unvalidated' ? 'question'
        : validation === 'error' ? 'error' : 'check';
}

/**
 * What the control says on hover: the verdict when there is one, and what pressing it will do.
 *
 * Never states a verdict while edits are staged - the last result described a document that no
 * longer exists.
 */
export function validateTitle(
    validation: ValidationState, problemCount: number, pendingCount: number,
): string {
    const action = 'Press to check again.';

    if (pendingCount > 0 || validation === 'unvalidated') {
        return 'Validate - check the file and everything staged, without writing.';
    }

    if (validation === 'ok') {
        return `Validate - no problems found. ${action}`;
    }

    const plural = problemCount === 1 ? 'problem' : 'problems';
    return `Validate - ${problemCount} ${plural} found. ${action}`;
}
