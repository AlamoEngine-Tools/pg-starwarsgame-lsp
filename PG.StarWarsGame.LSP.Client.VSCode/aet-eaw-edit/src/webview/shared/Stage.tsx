// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The stage's own chrome, as components: the three-slot row along an edge and the flyout a pill on
// that row opens.
//
// The markup was written by hand at every use - fifteen slots and five flyouts in the model
// preview, one more flyout in the story graph - and the class strings carry meaning a typo loses
// silently: drop `stage-slot-end` and the camera plate stops hugging the right edge.
// The styles are `stageRowCss` and `stageFlyoutCss`; where a flyout goes is `placeStageFlyout`.

import * as l10n from "@vscode/l10n";
import {
    CSSProperties,
    ReactNode,
    Ref,
    RefObject,
    useCallback,
    useEffect,
    useLayoutEffect,
    useRef,
    useState,
} from "react";

import { IconButton } from "./Button";
import {
    placeStageFlyout,
    type StageFlyoutPlacement,
} from "./stageFlyoutPlace";

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
    /**
     * The pill that opens it. The flyout opens at that pill - below one on the top edge, above one
     * on the bottom edge - and follows it when the stage or the row is laid out again.
     */
    anchor: RefObject<HTMLElement | null>;
    /** A variant such as `sizeable`. */
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

/** The panel a stage pill opens beside itself. */
export function StageFlyout({
    title,
    anchor,
    className,
    count,
    tools,
    edges,
    style,
    ref,
    onClose,
    children,
}: StageFlyoutProps): React.JSX.Element {
    const classes = ["stage-flyout", className].filter(Boolean).join(" ");

    const box = useRef<HTMLDivElement | null>(null);
    const [at, setAt] = useState<StageFlyoutPlacement | null>(null);

    // One element, two readers: the caller's ref is for its outside-click check, this one is for
    // measuring.
    const setBox = useCallback(
        (element: HTMLDivElement | null): void => {
            box.current = element;

            if (typeof ref === "function") {
                ref(element);
            } else if (ref) {
                ref.current = element;
            }
        },
        [ref],
    );

    const place = useCallback((): void => {
        const flyout = box.current;
        const pill = anchor.current;
        const stage = flyout?.offsetParent;

        if (!flyout || !pill || !(stage instanceof HTMLElement)) {
            return;
        }

        // From inside the stage's border, which is where an absolutely placed child is measured
        // from.
        const outer = stage.getBoundingClientRect();
        const x = outer.left + stage.clientLeft;
        const y = outer.top + stage.clientTop;
        const pillBox = pill.getBoundingClientRect();
        const plateBox = (
            pill.closest(".stage-chrome") ?? pill
        ).getBoundingClientRect();

        const next = placeStageFlyout({
            pill: {
                left: pillBox.left - x,
                right: pillBox.right - x,
                top: pillBox.top - y,
                bottom: pillBox.bottom - y,
            },
            plate: { top: plateBox.top - y, bottom: plateBox.bottom - y },
            width: flyout.offsetWidth,
            stage: { width: stage.clientWidth, height: stage.clientHeight },
        });

        // The same answer is the same object, or placing after every render would render again.
        setAt((was) =>
            was !== null &&
            was.left === next.left &&
            was.side === next.side &&
            was.offset === next.offset &&
            was.maxHeight === next.maxHeight &&
            was.tail === next.tail
                ? was
                : next,
        );
    }, [anchor]);

    // Before the browser paints, and after EVERY render rather than once: the pill moves when its
    // row gains or loses a control, and nothing announces that but the render that did it.
    useLayoutEffect(() => {
        place();
    });

    // A resized stage moves the pill, and a flyout the reader sizes changes its own width.
    useEffect(() => {
        const flyout = box.current;
        const stage = flyout?.offsetParent;

        if (!flyout || !stage || typeof ResizeObserver === "undefined") {
            return;
        }

        const observer = new ResizeObserver(place);
        observer.observe(stage);
        observer.observe(flyout);

        return () => observer.disconnect();
    }, [place]);

    const placed: CSSProperties =
        at === null
            ? // Rendered to be measured, invisible until it has somewhere to be.
              { visibility: "hidden" }
            : at.side === "below"
              ? { left: at.left, top: at.offset, maxHeight: at.maxHeight }
              : { left: at.left, bottom: at.offset, maxHeight: at.maxHeight };

    return (
        <div
            className={classes}
            role="dialog"
            aria-label={title}
            ref={setBox}
            style={{ ...style, ...placed }}
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
            {/* What joins the flyout to its pill, the way a speech bubble is joined to its speaker.
                Only once placed: before that there is no pill to point at. */}
            {at !== null && (
                <span
                    className={`stage-flyout-tail at-${at.side === "below" ? "top" : "bottom"}`}
                    style={{ left: at.tail }}
                    aria-hidden="true"
                />
            )}
        </div>
    );
}
