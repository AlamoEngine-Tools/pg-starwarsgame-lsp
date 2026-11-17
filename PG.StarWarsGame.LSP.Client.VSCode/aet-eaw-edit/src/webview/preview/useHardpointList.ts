// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The hardpoint list's own state: its filter, the type headings folded away, the cards grouped
// by type, and the card scrolled to and unfolded when its hardpoint is aimed at.

import {
    useEffect,
    useMemo,
    useState,
    type Dispatch,
    type SetStateAction,
} from "react";
import { groupHardpoints, groupOf } from "./hardpointGroups";
import { searchIsOpen } from "./treeSearch";
import { type HardpointCard } from "./hardpointCards";
import { type WeaponRow } from "./weaponRows";

/** What useHardpointList reads from the rest of the preview. */
export interface HardpointListInputs {
    cards: HardpointCard[];
    fireTarget: string;
    hardpointSearchPressed: boolean;
    infoCard: string | null;
    looseWeapons: WeaponRow[];
    setInfoCard: Dispatch<SetStateAction<string | null>>;
}

export type HardpointList = ReturnType<typeof useHardpointList>;

export function useHardpointList({
    cards,
    fireTarget,
    hardpointSearchPressed,
    infoCard,
    looseWeapons,
    setInfoCard,
}: HardpointListInputs) {
    /* Type headings the reader has folded away, by type. A hull with ten laser hardpoints is ten
       cards you scroll past on the way to its engines. */
    const [foldedGroups, setFoldedGroups] = useState<ReadonlySet<string>>(
        new Set(),
    );

    /* The hardpoint list's own filter, in the tree's shape - see `searchIsOpen` for why a pattern
       keeps its own box open whatever the button was left saying. */
    const [hardpointFilter, setHardpointFilter] = useState("");

    const hardpointSearchOpen = searchIsOpen(
        hardpointSearchPressed,
        hardpointFilter,
    );

    /* Gathered by type, and narrowed by the filter. A Star Destroyer's eight laser hardpoints are
       eight cards that differ only by which corner they sit on. */
    const hardpointGroups = useMemo(
        () => groupHardpoints(cards, hardpointFilter),
        [cards, hardpointFilter],
    );

    /* A flyout is anchored to a card, so it cannot outlive the card being on screen - filtering it
       away or folding its group leaves a panel pinned to nothing, describing something the reader
       can no longer see. The same rule the tree's details flyout follows.

       BOTH kinds of card, since the unit weapons got the info button too. Asking the hardpoint
       groups alone shut a weapon card's flyout the instant it opened: a unit weapon is in no
       hardpoint group by definition, so the check read as "filtered away" every time. */
    useEffect(() => {
        if (infoCard === null) {
            return;
        }

        const onScreen =
            groupOf(hardpointGroups, infoCard) !== null ||
            looseWeapons.some((row) => row.id === infoCard);

        if (!onScreen) {
            setInfoCard(null);
        }
    }, [infoCard, hardpointGroups, looseWeapons]);

    /**
     * Brings the aimed-at hardpoint's card into view.
     *
     * Clicking a targeting mark on the model picks that hardpoint, and on a hull with thirty of
     * them the card saying so was as likely as not somewhere below the fold - so the mark lit up
     * and the panel appeared to do nothing. Its group is unfolded first, because a card inside a
     * folded heading is not scrollable to at all.
     *
     * `nearest` rather than `center`: a card already on screen must not be yanked about, and this
     * runs for a click on the card itself just as much as for a click on the mark.
     */
    useEffect(() => {
        if (fireTarget === "hull") {
            return;
        }

        const group = groupOf(hardpointGroups, fireTarget);
        if (group !== null) {
            setFoldedGroups((current) => {
                if (!current.has(group)) {
                    return current;
                }

                const next = new Set(current);
                next.delete(group);
                return next;
            });
        }

        // After the fold has been taken off, or the card is still not laid out.
        const at = requestAnimationFrame(() => {
            document
                .querySelector(`[data-card="${CSS.escape(fireTarget)}"]`)
                ?.scrollIntoView({ block: "nearest" });
        });

        return () => cancelAnimationFrame(at);
    }, [fireTarget, hardpointGroups]);

    return {
        foldedGroups,
        setFoldedGroups,
        hardpointFilter,
        setHardpointFilter,
        hardpointSearchOpen,
        hardpointGroups,
    };
}
