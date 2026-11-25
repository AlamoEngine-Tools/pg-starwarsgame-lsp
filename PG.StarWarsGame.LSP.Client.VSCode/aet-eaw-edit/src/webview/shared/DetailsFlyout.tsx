// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The flyout a row's info pill opens beside the dock: what a hardpoint card measures, what an
// ability declares, what a story type's documentation says.
//
// The markup was written by hand at each use in the model preview. It is one component since the
// story graph's palette took the same flyout, and since it gained its tail - the point on its edge
// that ends at the pill that opened it, which every use has to draw in the same place. The styles
// are `detailsFlyoutCss`; where it goes is `anchorFlyout`.

import * as l10n from "@vscode/l10n";
import type { ReactNode, Ref } from "react";

import { IconButton } from "./Button";
import type { AnchorPlacement } from "./flyoutAnchor";

export interface DetailsFlyoutProps {
    /** What the flyout is about, in its head. */
    title: ReactNode;

    /** A second name or a kind after the title, muted. */
    subtitle?: ReactNode;

    /**
     * Where it goes, or null while it has not been measured: it is drawn hidden first, because a
     * box has to be rendered to be measured and one painted frame in the wrong place is a jump.
     */
    at: AnchorPlacement | null;

    /** For measuring, and for an outside-click check. */
    ref?: Ref<HTMLDivElement>;

    className?: string;
    onClose: () => void;
    children: ReactNode;
}

export function DetailsFlyout({
    title,
    subtitle,
    at,
    ref,
    className,
    onClose,
    children,
}: DetailsFlyoutProps): React.JSX.Element {
    return (
        <div
            className={["details-flyout", className].filter(Boolean).join(" ")}
            role="dialog"
            ref={ref}
            style={
                at === null
                    ? { visibility: "hidden" }
                    : { left: at.left, top: at.top }
            }
        >
            <div className="details-head">
                <span className="details-title">{title}</span>
                {subtitle !== undefined && (
                    <span className="details-subtitle">{subtitle}</span>
                )}
                <IconButton
                    icon="close"
                    title={l10n.t("Close")}
                    onClick={onClose}
                />
            </div>
            <div className="details-body">{children}</div>
            {/* What joins the flyout to its pill. Only once placed: before that there is no pill
                to point at. */}
            {at !== null && (
                <span
                    className="details-tail"
                    style={{ top: at.tail }}
                    aria-hidden="true"
                />
            )}
        </div>
    );
}
