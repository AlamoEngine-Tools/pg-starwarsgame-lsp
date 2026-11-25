// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The flyouts and popovers: which are open, their boxes, where the row-anchored ones sit, and the
// rules that close them - Escape for all, an outside click for the searches and the model info, and
// the selection let go only once no flyout is left for Escape to close.

import {
    useCallback,
    useEffect,
    useLayoutEffect,
    useRef,
    useState,
} from "react";
import { anchorFlyout, type AnchorPlacement } from "../shared/flyoutAnchor";

/** What useFlyouts reads from the rest of the preview. */
export interface FlyoutsInputs {
    clearSelection: () => void;
    detailsRow: string | null;
    selected: ReadonlySet<string>;
}

export type Flyouts = ReturnType<typeof useFlyouts>;

export function useFlyouts({
    clearSelection,
    detailsRow,
    selected,
}: FlyoutsInputs) {
    /** Whether the room's controls are unfolded. Closed on open: they are not why you came here. */
    const [worldOpen, setWorldOpen] = useState(false);

    /**
     * Whether the camera's controls are unfolded.
     *
     * Its own panel rather than a section of the scene's: where you look FROM is not part of the
     * room you are looking at, and the two were mixed in one list that had grown long enough to
     * scroll past whichever half you wanted.
     */
    const [cameraOpen, setCameraOpen] = useState(false);
    /* The two Gameplay tools on the bottom edge. Kept apart from the camera and scene flyouts
       above: those describe the view and may be open together with anything, while these describe
       the subject and sit on the opposite edge. */
    const [abilitiesOpen, setAbilitiesOpen] = useState(false);
    const [attackOpen, setAttackOpen] = useState(false);
    const cameraRef = useRef<HTMLDivElement | null>(null);
    const abilitiesRef = useRef<HTMLDivElement | null>(null);
    const attackRef = useRef<HTMLDivElement | null>(null);

    /* The pill that opens each stage flyout. A flyout opens at its pill, so it has to be told
       which one that is - the pills are on the stage's rows, the flyouts beside them. */
    const worldPillRef = useRef<HTMLButtonElement | null>(null);
    const cameraPillRef = useRef<HTMLButtonElement | null>(null);
    const abilitiesPillRef = useRef<HTMLButtonElement | null>(null);
    const attackPillRef = useRef<HTMLButtonElement | null>(null);

    /** Whether the model's identity flyout is showing. Closed on open - it is a check, not a tool. */
    const [infoOpen, setInfoOpen] = useState(false);
    const infoRef = useRef<HTMLDivElement | null>(null);

    /* Whether the reader has pressed the tree's magnifier. NOT whether the box is showing - see
       `searchIsOpen`, which keeps it open while a pattern would otherwise be hidden behind it. */
    const [searchPressed, setSearchPressed] = useState(false);

    /* Which hardpoint card has its measurements open. One at a time: two of them open turns the
       list back into the wall of prose that putting them behind a button was meant to end. */
    const [infoCard, setInfoCard] = useState<string | null>(null);
    const cardInfoRef = useRef<HTMLDivElement | null>(null);
    const [cardInfoAt, setCardInfoAt] = useState<AnchorPlacement | null>(null);

    /* The same, for an ability card. Its own slot rather than sharing `infoCard`: the two lists are
       open at once - the abilities are a stage flyout - and one id could not say which was meant. */
    const [infoAbility, setInfoAbility] = useState<string | null>(null);
    const abilityInfoRef = useRef<HTMLDivElement | null>(null);
    const [abilityInfoAt, setAbilityInfoAt] = useState<AnchorPlacement | null>(
        null,
    );
    const [hardpointSearchPressed, setHardpointSearchPressed] = useState(false);
    const hardpointSearchRef = useRef<HTMLDivElement | null>(null);

    /**
     * The scene flyout's box.
     *
     * Escape closes it, but a click OUTSIDE deliberately does not: these are settings you adjust
     * while watching what they do to the model, and dismissing the panel the moment you touch the
     * viewport would make the two impossible to use together. It has its own close button instead.
     */
    const worldRef = useRef<HTMLDivElement | null>(null);

    /**
     * The tree's search element, for the click-outside that folds it away again.
     *
     * Unlike the stage flyouts above, an outside click DOES close this one: it is a control you
     * open, type in and leave, not a set of dials you work while watching the model. It closes only
     * when empty, though - `searchIsOpen` would reopen it on the next render otherwise, and the
     * press would read as a control that does nothing.
     */
    const searchRef = useRef<HTMLDivElement | null>(null);

    useEffect(() => {
        if (!hardpointSearchPressed) {
            return;
        }

        const onDown = (event: MouseEvent): void => {
            if (!hardpointSearchRef.current?.contains(event.target as Node)) {
                setHardpointSearchPressed(false);
            }
        };

        document.addEventListener("mousedown", onDown);
        return () => document.removeEventListener("mousedown", onDown);
    }, [hardpointSearchPressed]);

    useEffect(() => {
        if (!searchPressed) {
            return;
        }

        const onDown = (event: MouseEvent): void => {
            if (!searchRef.current?.contains(event.target as Node)) {
                setSearchPressed(false);
            }
        };

        document.addEventListener("mousedown", onDown);
        return () => document.removeEventListener("mousedown", onDown);
    }, [searchPressed]);

    // Both stage flyouts, one rule. Escape closes both; an outside click deliberately closes
    // neither - see above. They can be open TOGETHER on purpose: setting up a shot means moving the
    // light and the camera against each other, and a panel that shuts the other one turns that into
    // a trip back to the button every time. Their pills are at opposite ends of the top edge, so
    // on a stage wide enough for both they do not overlap.
    useEffect(() => {
        if (!worldOpen && !cameraOpen && !abilitiesOpen && !attackOpen) {
            return;
        }

        const onKey = (event: KeyboardEvent): void => {
            if (event.key === "Escape") {
                setWorldOpen(false);
                setCameraOpen(false);
                setAbilitiesOpen(false);
                setAttackOpen(false);
            }
        };

        document.addEventListener("keydown", onKey);

        return () => document.removeEventListener("keydown", onKey);
    }, [worldOpen, cameraOpen, abilitiesOpen, attackOpen]);

    /**
     * Closes the flyout on Escape or a click anywhere else.
     *
     * A popover that can only be dismissed by finding the button again is a popover in the way. The
     * button's own click is excluded, or its toggle would fire twice and immediately reopen it.
     */
    useEffect(() => {
        if (!infoOpen) {
            return;
        }

        const dismiss = (event: Event): void => {
            const target = event.target as Node | null;
            const onButton =
                target instanceof Element &&
                target.closest(".header-left") !== null;

            if (
                !onButton &&
                infoRef.current?.contains(target ?? null) !== true
            ) {
                setInfoOpen(false);
            }
        };

        const onKey = (event: KeyboardEvent): void => {
            if (event.key === "Escape") {
                setInfoOpen(false);
            }
        };

        document.addEventListener("pointerdown", dismiss);
        document.addEventListener("keydown", onKey);

        return () => {
            document.removeEventListener("pointerdown", dismiss);
            document.removeEventListener("keydown", onKey);
        };
    }, [infoOpen]);

    /**
     * Escape lets go of the selection.
     *
     * After the panels, so a flyout still closes first: Escape means "back out of the innermost
     * thing", and dropping a selection while a panel is open would be backing out of the wrong one.
     */
    useEffect(() => {
        if (selected.size === 0) {
            return;
        }

        const onKey = (event: KeyboardEvent): void => {
            if (
                event.key === "Escape" &&
                !worldOpen &&
                !cameraOpen &&
                !abilitiesOpen &&
                !attackOpen &&
                detailsRow === null
            ) {
                clearSelection();
            }
        };

        document.addEventListener("keydown", onKey);

        return () => document.removeEventListener("keydown", onKey);
    }, [
        selected,
        worldOpen,
        cameraOpen,
        abilitiesOpen,
        attackOpen,
        detailsRow,
        clearSelection,
    ]);

    /**
     * Where a flyout anchored to a row in the dock goes.
     *
     * Measured rather than positioned in CSS, because the row lives inside a scroller: a flyout
     * parented to it would be clipped by the list, and one parented outside has no idea where the
     * row ended up. Both are read at the moment of placing, so a resized dock or a scrolled list
     * moves it rather than leaving it pointing at nothing.
     *
     * Shared by the hardpoint card's info and the ability card's, because they are the same shape
     * answering the same question about different rows - and a second copy of this would be a
     * second thing to keep in step with `anchorFlyout`.
     */
    const placeBesideRow = useCallback(
        (selector: string, box: HTMLElement | null): AnchorPlacement | null => {
            const button = document.querySelector(selector);
            const dock = document.querySelector(".right-dock");

            if (button === null || dock === null || box === null) {
                return null;
            }

            const row = button.getBoundingClientRect();

            return anchorFlyout({
                row: { top: row.top, bottom: row.bottom },
                dockLeft: dock.getBoundingClientRect().left,
                window: {
                    width: window.innerWidth,
                    height: window.innerHeight,
                },
                size: { width: box.offsetWidth, height: box.offsetHeight },
            });
        },
        [],
    );

    const placeInfo = useCallback(() => {
        if (infoCard === null) {
            return;
        }

        const at = placeBesideRow(
            `[data-card="${CSS.escape(infoCard)}"] .card-info-btn`,
            cardInfoRef.current,
        );

        if (at !== null) {
            setCardInfoAt(at);
        }
    }, [infoCard, placeBesideRow]);

    const placeAbilityInfo = useCallback(() => {
        if (infoAbility === null) {
            return;
        }

        const at = placeBesideRow(
            `[data-ability="${CSS.escape(infoAbility)}"] .card-info-btn`,
            abilityInfoRef.current,
        );

        if (at !== null) {
            setAbilityInfoAt(at);
        }
    }, [infoAbility, placeBesideRow]);

    useLayoutEffect(() => {
        placeAbilityInfo();
    }, [placeAbilityInfo]);

    // The abilities flyout is closed by Escape and by its own button; when it goes, so does this.
    useEffect(() => {
        if (!abilitiesOpen) {
            setInfoAbility(null);
        }
    }, [abilitiesOpen]);

    // Same terms as the details flyout: before paint, and again while the dock scrolls under it.
    useLayoutEffect(() => {
        placeInfo();
    }, [placeInfo]);

    useEffect(() => {
        if (infoCard === null) {
            return;
        }

        const onMove = (): void => placeInfo();

        window.addEventListener("resize", onMove);
        document
            .querySelector(".dock-content")
            ?.addEventListener("scroll", onMove);

        return () => {
            window.removeEventListener("resize", onMove);
            document
                .querySelector(".dock-content")
                ?.removeEventListener("scroll", onMove);
        };
    }, [infoCard, placeInfo]);

    return {
        worldOpen,
        setWorldOpen,
        cameraOpen,
        setCameraOpen,
        abilitiesOpen,
        setAbilitiesOpen,
        attackOpen,
        setAttackOpen,
        cameraRef,
        abilitiesRef,
        attackRef,
        worldPillRef,
        cameraPillRef,
        abilitiesPillRef,
        attackPillRef,
        infoOpen,
        setInfoOpen,
        infoRef,
        searchPressed,
        setSearchPressed,
        infoCard,
        setInfoCard,
        cardInfoRef,
        cardInfoAt,
        infoAbility,
        setInfoAbility,
        abilityInfoRef,
        abilityInfoAt,
        hardpointSearchPressed,
        setHardpointSearchPressed,
        hardpointSearchRef,
        worldRef,
        searchRef,
    };
}
