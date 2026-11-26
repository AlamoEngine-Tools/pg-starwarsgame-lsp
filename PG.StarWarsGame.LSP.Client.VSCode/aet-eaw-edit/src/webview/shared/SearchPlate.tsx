// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The search of a list or a page: a plate on the corner of what it filters.
//
// Closed, it is one row of icons: a magnifier, the list's own quick filters, and a filter icon.
// The magnifier shows and hides the field and does nothing else - a pattern stays in force behind
// a closed plate. The filter icon is what says so: plain while nothing is set, it becomes the
// reset as soon as a pattern or a quick filter narrows the list, and pressing it clears them all.
// The field opens to the LEFT of icons that never move, with the three ways of reading a pattern
// inside it, the way an editor's find widget has them.
//
// It was the model tree's, then copied by hand for the hardpoint list. It is one component since
// the story graph's palette took it as well, and it is the shape every search starts from. The
// styles are `searchPlateCss`.

import * as l10n from "@vscode/l10n";
import {
    type ReactNode,
    useCallback,
    useEffect,
    useLayoutEffect,
    useRef,
    useState,
} from "react";

import { IconButton } from "./Button";
import { type ChoiceOption } from "./choice";
import { ModeSelector } from "./ModeSelector";
import { type SearchMode } from "./searchPattern";
import { placePlateField, type PlateFieldPlacement } from "./searchPlateField";

/**
 * The three ways a pattern is read, in the wording the localisation search shipped with.
 *
 * Each is drawn as the characters it means rather than as an icon: `ab` for plain text, `*?` for
 * the two wildcards, `.*` for a regular expression. The icons before them said the wrong things -
 * a letter-case glyph reads as "case sensitive" for a search that is not, and an asterisk drawn
 * small is a star.
 */
export const SEARCH_MODES: ChoiceOption<SearchMode>[] = [
    {
        id: "text",
        label: l10n.t("text"),
        mark: "ab",
        title: l10n.t("Plain text search"),
    },
    {
        id: "wildcard",
        label: l10n.t("wildcard"),
        mark: "*?",
        title: l10n.t("Wildcard: * matches any text, ? matches one character"),
    },
    {
        id: "regex",
        label: l10n.t("regex"),
        mark: ".*",
        title: l10n.t("Regular expression (case-insensitive)"),
    },
];

export interface SearchPlateProps {
    /** Whether the field is showing. The magnifier's state, and nothing else. */
    open: boolean;
    pattern: string;

    /** How the pattern is read. */
    mode: SearchMode;

    /**
     * Whether anything narrows the list: a pattern, or a quick filter away from its resting state.
     * A toggle switched off counts. It is what turns the filter icon into the reset.
     */
    filtered: boolean;
    placeholder: string;

    /** The magnifier's tooltip while the plate is closed. The placeholder, when there is none. */
    title?: string;

    /** Why the pattern will not compile, on the field. */
    error?: string;

    onPattern: (pattern: string) => void;
    onMode: (mode: SearchMode) => void;

    /** The reader pressed the magnifier, or Escape in the field. The pattern is not touched. */
    onOpen: (open: boolean) => void;

    /** The reader pressed the reset: clear the pattern and put every quick filter back. */
    onReset: () => void;

    /**
     * Where the plate stands. `floating` on the corner of a list that scrolls inside a pane of its
     * own; `sticky` at the foot of a scroller it is the last child of.
     */
    variant?: "floating" | "sticky";

    /**
     * The field opens beside the plate, to its left and over whatever is there, instead of inside
     * it. For a plate in a dock, whose column is too narrow for a field and its icons on one row.
     */
    beside?: boolean;

    /** The list's own quick filters - which kinds it shows - after the magnifier. Three at most. */
    children?: ReactNode;
}

function samePlace(
    a: PlateFieldPlacement | null,
    b: PlateFieldPlacement,
): boolean {
    return (
        a !== null &&
        a.left === b.left &&
        a.top === b.top &&
        a.width === b.width &&
        a.height === b.height
    );
}

export function SearchPlate({
    open,
    pattern,
    mode,
    filtered,
    placeholder,
    title,
    error,
    onPattern,
    onMode,
    onOpen,
    onReset,
    variant = "floating",
    beside = false,
    children,
}: SearchPlateProps): React.JSX.Element {
    const plate = useRef<HTMLDivElement | null>(null);
    const [fieldAt, setFieldAt] = useState<PlateFieldPlacement | null>(null);
    const detached = open && beside;

    /* The field beside the plate is placed against the window, so nothing positions it but this:
       measured after every render, and again when the window or anything around the plate moves. */
    const place = useCallback((): void => {
        const box = plate.current?.getBoundingClientRect();
        if (box === undefined) {
            return;
        }

        const next = placePlateField({
            left: box.left,
            top: box.top,
            height: box.height,
        });
        setFieldAt((was) => (samePlace(was, next) ? was : next));
    }, []);

    useLayoutEffect(() => {
        if (detached) {
            place();
        }
    });

    useEffect(() => {
        if (!detached) {
            return;
        }

        window.addEventListener("resize", place);
        window.addEventListener("scroll", place, true);

        return () => {
            window.removeEventListener("resize", place);
            window.removeEventListener("scroll", place, true);
        };
    }, [detached, place]);

    /* An outside press folds an EMPTY field away: it is a control the reader opens, types in and
       leaves. A field holding a pattern stays until the magnifier or Escape closes it - the reader
       is working the list with it, and a field that vanished on every row press would have to be
       reopened for every refinement. */
    useEffect(() => {
        if (!open || pattern !== "") {
            return;
        }

        const onDown = (event: MouseEvent): void => {
            if (!plate.current?.contains(event.target as Node)) {
                onOpen(false);
            }
        };

        document.addEventListener("mousedown", onDown);
        return () => document.removeEventListener("mousedown", onDown);
    }, [open, pattern, onOpen]);

    const boxStyle =
        !detached || fieldAt === null
            ? detached
                ? ({ visibility: "hidden" } as const)
                : undefined
            : {
                  left: fieldAt.left,
                  top: fieldAt.top,
                  width: fieldAt.width,
                  height: fieldAt.height,
              };

    return (
        <div
            className={
                `search-plate ${variant}` +
                (open ? " open" : "") +
                (detached ? " beside" : "")
            }
            ref={plate}
        >
            {/* The field grows into the space to the LEFT of the icons, which never move. That is
                what makes the strip a fixed anchor rather than a row that rearranges itself. */}
            {open && (
                <div
                    className={"search-plate-box" + (beside ? " beside" : "")}
                    style={boxStyle}
                >
                    <input
                        className="search-plate-field"
                        type="text"
                        autoFocus
                        value={pattern}
                        placeholder={placeholder}
                        // Without this the list just empties, which reads as "no matches" rather
                        // than "that pattern is not valid" - the two are identical on screen.
                        aria-invalid={error !== undefined}
                        title={error}
                        onChange={(event) => onPattern(event.target.value)}
                        onKeyDown={(event) => {
                            if (event.key === "Escape") {
                                event.stopPropagation();
                                onOpen(false);
                            }
                        }}
                    />
                    {/* One of three, so a radiogroup rather than three toggles each reporting
                        its own pressed state. */}
                    <div className="search-plate-modes">
                        <ModeSelector
                            label={l10n.t("How the pattern is read")}
                            value={mode}
                            options={SEARCH_MODES}
                            onSelect={onMode}
                        />
                    </div>
                </div>
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
                            ? l10n.t("Close the search")
                            : (title ?? placeholder)
                    }
                    onClick={() => onOpen(!open)}
                />
                {children}
                {/* Plain and dead while nothing is set; the reset once something is. The closed
                    plate shows the quick filters but not the pattern, so the reset names it. */}
                <IconButton
                    icon={filtered ? "clearFilter" : "filter"}
                    className={"search-plate-reset" + (filtered ? " set" : "")}
                    title={
                        pattern === ""
                            ? l10n.t("Clear all filters")
                            : l10n.t("Clear all filters - {0}", pattern)
                    }
                    disabled={!filtered}
                    disabledReason={l10n.t(
                        "Clear all filters - no filters are set",
                    )}
                    onClick={onReset}
                />
            </div>
        </div>
    );
}
