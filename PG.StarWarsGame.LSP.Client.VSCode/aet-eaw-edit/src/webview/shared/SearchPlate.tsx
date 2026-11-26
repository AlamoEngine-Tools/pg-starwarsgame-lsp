// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The search of a list in a dock: a plate with a magnifier and the list's own toggles, whose field
// opens to the LEFT of icons that never move.
//
// It was the model tree's, then copied by hand for the hardpoint list. It is one component since
// the story graph's palette took it as well, and it is the shape every list search in a dock
// starts from: closed, it costs the list nothing but a corner; open, the field takes the room
// beside the icons, and nothing the reader was pointing at has shifted. The styles are
// `searchPlateCss`.

import * as l10n from "@vscode/l10n";
import type { ReactNode, Ref } from "react";

import { IconButton } from "./Button";

/**
 * Whether the plate's field is showing.
 *
 * Not simply the magnifier's state. A non-empty pattern keeps its own field open however the
 * magnifier was left, because the alternative is a list silently narrowed by text nobody can see -
 * and the way out of that state is to edit the very text the collapse just hid.
 */
export function searchPlateOpen(pressed: boolean, pattern: string): boolean {
    // Length, not `trim()`. A pattern of one space matches nothing and empties the list, which is
    // the loudest version of this bug rather than an edge case of it.
    return pressed || pattern.length > 0;
}

export interface SearchPlateProps {
    /** Whether the field is showing - `searchPlateOpen` of the magnifier's state and the pattern. */
    open: boolean;
    pattern: string;
    placeholder: string;

    /** The magnifier's tooltip while the plate is closed. The placeholder, when there is none. */
    title?: string;

    onPattern: (pattern: string) => void;

    /**
     * The reader pressed the magnifier, or Escape in the field. Closing has cleared the pattern
     * first: a pattern kept behind a closed plate would reopen it at once, and the press would
     * appear to do nothing.
     */
    onOpen: (open: boolean) => void;

    /**
     * Where the plate stands. `floating` on the corner of a list that scrolls inside a pane of its
     * own; `sticky` at the foot of a scroller it is the last child of; `inline` in the flow above
     * a list that has no box to float over.
     */
    variant?: "floating" | "sticky" | "inline";

    /** For the outside-click that folds an empty plate away again. */
    ref?: Ref<HTMLDivElement>;

    /** The list's own toggles - which kinds it shows - after the magnifier. */
    children?: ReactNode;
}

export function SearchPlate({
    open,
    pattern,
    placeholder,
    title,
    onPattern,
    onOpen,
    variant = "floating",
    ref,
    children,
}: SearchPlateProps): React.JSX.Element {
    const close = (): void => {
        onPattern("");
        onOpen(false);
    };

    return (
        <div
            className={`search-plate ${variant}` + (open ? " open" : "")}
            ref={ref}
        >
            {/* The field grows into the space to the LEFT of the icons, which never move. That is
                what makes the strip a fixed anchor rather than a row that rearranges itself. */}
            {open && (
                <input
                    className="search-plate-field"
                    type="text"
                    autoFocus
                    value={pattern}
                    placeholder={placeholder}
                    onChange={(event) => onPattern(event.target.value)}
                    onKeyDown={(event) => {
                        if (event.key === "Escape") {
                            event.stopPropagation();
                            close();
                        }
                    }}
                />
            )}

            {/* The icons are ONE flex item, so the plate can only wrap between the field and the
                strip - never in the middle of the strip. */}
            <div className="search-plate-icons">
                <IconButton
                    icon="search"
                    className={"search-plate-toggle" + (open ? " active" : "")}
                    expanded={open}
                    title={
                        open
                            ? l10n.t("Close the filter")
                            : (title ?? placeholder)
                    }
                    onClick={() => (open ? close() : onOpen(true))}
                />
                {children}
            </div>
        </div>
    );
}
