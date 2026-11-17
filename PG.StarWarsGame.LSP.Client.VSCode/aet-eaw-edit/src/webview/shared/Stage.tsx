// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The stage's own chrome, as components: the three-slot row along an edge and the flyout a corner
// button opens.
//
// The markup was written by hand at every use - fifteen slots and five flyouts in the model
// preview, one more flyout in the story graph - and the class strings carry meaning a typo loses
// silently: drop `stage-slot-end` and the camera plate stops hugging the right edge, drop
// `from-bottom` and a flyout opens at the top of the stage while its button is at the bottom.
// The styles are `stageRowCss` and `stageFlyoutCss`.

import * as l10n from "@vscode/l10n";
import { CSSProperties, ReactNode, Ref } from "react";

import { IconButton } from "./Button";

export interface StageSlotsProps {
    /** Which edge of the stage the row runs along. The top answers how you are looking, the bottom what the subject is doing. */
    edge: "top" | "bottom";
    start?: ReactNode;
    middle?: ReactNode;
    end?: ReactNode;
}

/**
 * One edge of the stage, as three slots.
 *
 * Every slot is drawn whether or not it holds anything: the two side slots being EQUAL is the whole
 * of what centres the middle one, so an empty side has to stay to balance the other.
 */
export function StageSlots({
    edge,
    start,
    middle,
    end,
}: StageSlotsProps): React.JSX.Element {
    return (
        <div className={`stage-row stage-${edge}`}>
            <span className="stage-slot">{start}</span>
            <span className="stage-slot stage-slot-mid">{middle}</span>
            <span className="stage-slot stage-slot-end">{end}</span>
        </div>
    );
}

export interface StageFlyoutProps {
    /** The head's text, and the dialog's accessible name. */
    title: string;
    /** The corner it hangs from, which is the side its button is on. */
    at: "left" | "mid" | "right";
    /** Opens upwards, for a button on the bottom edge. */
    fromBottom?: boolean;
    /** A variant such as `sizeable`. Added after the corner, never replacing it. */
    className?: string;
    /** A count after the title. It takes the head's free space, so the buttons follow it. */
    count?: ReactNode;
    /** Buttons in the head before the close, such as a reset. */
    tools?: ReactNode;
    /** Drawn on the box before the head - the resize edges of a flyout the reader can size. */
    edges?: ReactNode;
    style?: CSSProperties;
    /** For the outside-click check that closes it. */
    ref?: Ref<HTMLDivElement>;
    onClose: () => void;
    children: ReactNode;
}

/** The panel a stage-corner button opens beside itself. */
export function StageFlyout({
    title,
    at,
    fromBottom = false,
    className,
    count,
    tools,
    edges,
    style,
    ref,
    onClose,
    children,
}: StageFlyoutProps): React.JSX.Element {
    const classes = [
        "stage-flyout",
        fromBottom ? "from-bottom" : null,
        `on-${at}`,
        className,
    ]
        .filter(Boolean)
        .join(" ");

    return (
        <div
            className={classes}
            role="dialog"
            aria-label={title}
            ref={ref}
            style={style}
        >
            {edges}
            <div className="stage-flyout-head">
                {title}
                {count !== undefined && (
                    <span className="section-count header-right">{count}</span>
                )}
                {tools}
                <IconButton
                    icon="close"
                    title={l10n.t("Close")}
                    onClick={onClose}
                />
            </div>
            <div className="stage-flyout-body">{children}</div>
        </div>
    );
}
