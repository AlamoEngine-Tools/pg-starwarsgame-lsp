// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The pure half of the info badge: what a severity looks like.
//
import { type IconName } from './Icon';

// A control's explanation is worth having and worth reading ONCE. Printed under every setting it
// turns a panel of eight controls into a page of prose, and the reader scrolls past the thing they
// came for. The badge puts it behind a mark that says there is more, and colours that mark when
// what is behind it is a warning rather than an explanation.

export type BadgeSeverity = 'info' | 'warning' | 'error';

/** The icon for one severity. Each severity gets its own MARK, not just its own colour. */
export function badgeIcon(severity: BadgeSeverity): IconName {
    switch (severity) {
        case 'warning': return 'warning';
        case 'error': return 'error';
        default: return 'details';
    }
}
