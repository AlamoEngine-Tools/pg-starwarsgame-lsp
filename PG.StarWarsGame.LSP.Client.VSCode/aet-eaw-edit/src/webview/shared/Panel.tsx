// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// A titled box of content - a list, a code block, a card - as opposed to a .dock-tile, which is an
// ACTION. Its heading is the dock section heading, with the same ONE .title-end for a count and
// whatever sits at its end. Its look is .panel in dockChromeCss.

import { ReactNode } from "react";

export function Panel({
    title,
    count,
    end,
    flush,
    foot,
    className,
    style,
    children,
}: {
    title: string;
    /** Shown beside the title where the number tells the reader something. */
    count?: number | string;
    /** A switch or a button, packed against the far edge after the count. */
    end?: ReactNode;
    /** The body has no padding: a list or a table runs to the panel's edges. */
    flush?: boolean;
    /** A quiet line under the body - what is not shown, or where it comes from. */
    foot?: ReactNode;
    className?: string;
    /** For a panel a grid places. */
    style?: React.CSSProperties;
    children: ReactNode;
}): React.JSX.Element {
    return (
        <section
            className={className === undefined ? "panel" : `panel ${className}`}
            style={style}
            aria-label={title}
        >
            <div className="dock-section-title">
                <span className="dock-section-name">{title}</span>
                {(count !== undefined || end !== undefined) && (
                    <span className="title-end">
                        {count !== undefined && (
                            <span className="section-count">{count}</span>
                        )}
                        {end}
                    </span>
                )}
            </div>
            <div className={flush ? "panel-body flush" : "panel-body"}>
                {children}
            </div>
            {foot !== undefined && <div className="panel-foot">{foot}</div>}
        </section>
    );
}
