// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// A file the grid shows and does not edit (#339).
//
// A compiled DAT is what a build makes of the project, so the grid has nothing to write into it.
// The server says so when it hands over the rows, with the reason in words, and every control that
// would change the file reads it from here: it stays where it is, disabled, and says why - a
// control that vanished would leave the reader wondering whether the file could ever be edited.

import { createContext, useContext } from "react";

/** Why the file on screen cannot be edited, as each disabled control says it; null when it can. */
export const ReadOnlyContext = createContext<string | null>(null);

export function useReadOnly(): string | null {
    return useContext(ReadOnlyContext);
}

/**
 * A row-menu item that changes the file. Disabled for a file that is not edited, with the reason
 * as its tooltip; otherwise a plain menu item, with whatever disabled state it was given.
 */
export function EditMenuItem(
    props: React.ButtonHTMLAttributes<HTMLButtonElement>,
): React.JSX.Element {
    const readOnly = useReadOnly();
    return (
        <button
            role="menuitem"
            {...props}
            disabled={props.disabled === true || readOnly !== null}
            title={readOnly ?? props.title}
        />
    );
}
