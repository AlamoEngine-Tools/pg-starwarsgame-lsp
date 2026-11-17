// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Controls side by side on one line - a colour well beside its name, a card's buttons, a preset's
// actions. The style is the `.view-row` rule in `dockChromeCss`.

import { ReactNode } from "react";

export interface ViewRowProps {
    /** A variant such as `card-actions`. Added after the base class, never replacing it. */
    className?: string;
    children: ReactNode;
}

/** Controls side by side on one line, wrapping when the line runs out. */
export function ViewRow({
    className,
    children,
}: ViewRowProps): React.JSX.Element {
    return (
        <span
            className={
                className === undefined ? "view-row" : `view-row ${className}`
            }
        >
            {children}
        </span>
    );
}
