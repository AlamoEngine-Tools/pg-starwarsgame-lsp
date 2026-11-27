// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The page's own bar, across the top of an editor above its canvas and its dock alike (#222).
//
// What a page DOES to its document sat in the dock's header, beside the state the header is for:
// save at its left edge, the verdict and the hand-off at its right. The dock's three levels have no
// room for them - a header reports, the content lists, the foot steers the view - so they were
// squeezed in where there was a corner free, and one title already ran underneath the tag. They
// are the page's, not the dock's, and this is where the page keeps them.
//
// Glyphs only, as everywhere a glyph means the thing. Every page has the bar, and a control that
// appears on more than one page stands in the same spot on each: the bar takes its controls by
// name and places them itself, so no page can order them its own way. Save is first on the left,
// filled while changes are staged, with Discard beside it; the verdict is last on the right. What only some pages have -
// their own actions, after Save - grows inward from there, so its coming and going moves neither.
// A page that stages nothing has no Save. There is no Close: the tab's own cross closes the page.

import * as l10n from "@vscode/l10n";
import { ReactNode } from "react";

import { type Enablement, IconButton } from "./Button";

/** The page's save: what it writes, how much is waiting, and why it cannot be pressed when it cannot. */
export type ToolbarSave = Enablement & {
    /** What pressing it writes, as `Save - ...`. */
    title: string;
    /** The staged changes waiting to be written; drawn beside the glyph, and left out at zero. */
    pending: number;
    onSave: () => void;
};

/** Dropping what is staged, and why it cannot be pressed when it cannot. */
export type ToolbarDiscard = Enablement & {
    /** What pressing it drops, as `Discard - ...`. */
    title: string;
    onDiscard: () => void;
};

export interface PageToolbarProps {
    /** Left out on a page that stages nothing, which has nothing to save. */
    save?: ToolbarSave;
    /** Beside Save, on the pages that have it: the staged changes dropped instead of written. */
    discard?: ToolbarDiscard;
    /** The page's own actions, after Save: icon buttons. */
    actions?: ReactNode;
    /** The verdict on what is open. Every page has one, and it is always the last control. */
    verdict: ReactNode;
}

export function PageToolbar({
    save,
    discard,
    actions,
    verdict,
}: PageToolbarProps): React.JSX.Element {
    return (
        <div
            className="page-toolbar"
            role="toolbar"
            aria-label={l10n.t("Page actions")}
        >
            <span className="page-toolbar-start">
                {save !== undefined && <SaveButton save={save} />}
                {discard !== undefined &&
                    (discard.disabled === undefined ? (
                        <IconButton
                            icon="reset"
                            className="toolbar-discard"
                            title={discard.title}
                            onClick={discard.onDiscard}
                        />
                    ) : (
                        <IconButton
                            icon="reset"
                            className="toolbar-discard"
                            title={discard.title}
                            disabled={discard.disabled}
                            disabledReason={discard.disabledReason}
                            onClick={discard.onDiscard}
                        />
                    ))}
                {actions}
            </span>
            <span className="page-toolbar-end">{verdict}</span>
        </div>
    );
}

function SaveButton({ save }: { save: ToolbarSave }): React.JSX.Element {
    // Filled while something waits to be written, so staged work shows without reading the count.
    const className = "toolbar-save" + (save.pending > 0 ? " active" : "");
    // Nothing rather than a bare "0": a zero beside the glyph reads as a count to act on.
    const badge = save.pending > 0 ? ` ${save.pending}` : "";
    return save.disabled === undefined ? (
        <IconButton
            icon="save"
            className={className}
            title={save.title}
            badge={badge}
            onClick={save.onSave}
        />
    ) : (
        <IconButton
            icon="save"
            className={className}
            title={save.title}
            badge={badge}
            disabled={save.disabled}
            disabledReason={save.disabledReason}
            onClick={save.onSave}
        />
    );
}
