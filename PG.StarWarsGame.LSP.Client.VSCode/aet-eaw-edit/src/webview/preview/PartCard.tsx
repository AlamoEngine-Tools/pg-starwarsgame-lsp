// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// One named piece of game data, as a card: a hardpoint, a unit weapon, an ability, a death clone.
//
// They are the same kind of thing - a name, the tag a reader greps for, what it does on this model,
// and somewhere to jump to - so they are the same card, and a reader who has learnt one has learnt
// all four. The styles are the preview's own (`previewShell.ts`); only the preview lists parts.

import { ReactNode } from "react";

import { Icon } from "../shared/Icon";
import { ViewRow } from "../shared/ViewRow";

export interface PartCardProps {
    name: ReactNode;
    /** Before the name in the head: the mark the game draws over it, its icon, the tick that picks it. */
    lead?: ReactNode;
    /**
     * The jump to where it is defined, at the head's far end - away from the switches, because it
     * leaves the panel. Disabled rather than absent where there is nothing to jump to.
     */
    definition?: { title: string; disabled?: boolean; onGo: () => void };
    /** The game-data line under the head, in the card's monospace. Left out when null. */
    type?: ReactNode;
    /** The row of buttons at the card's foot. */
    actions?: ReactNode;
    /** A variant such as `weapon-card`. Added after the base class, never replacing it. */
    variant?: string;
    /** The part the attacker is aimed at, or the clone that would be left. */
    selected?: boolean;
    /** Destroyed. */
    gone?: boolean;
    title?: string;
    /** Each entry becomes a `data-` attribute on the card, for the specs that find it. */
    data?: Readonly<Record<string, string>>;
    onClick?: () => void;
    /** What it does on this model, between the type line and the actions. */
    children?: ReactNode;
}

export function PartCard({
    name,
    lead,
    definition,
    type,
    actions,
    variant,
    selected = false,
    gone = false,
    title,
    data,
    onClick,
    children,
}: PartCardProps): React.JSX.Element {
    const classes = [
        "hardpoint-card",
        variant,
        selected ? "selected" : null,
        gone ? "gone" : null,
    ]
        .filter(Boolean)
        .join(" ");

    const attributes = Object.fromEntries(
        Object.entries(data ?? {}).map(([key, value]) => [
            `data-${key}`,
            value,
        ]),
    );

    return (
        <li className={classes} {...attributes} title={title} onClick={onClick}>
            <div className="card-head">
                {lead}
                <span className="part-name">{name}</span>
                {definition !== undefined && (
                    <button
                        type="button"
                        className="goto-definition"
                        disabled={definition.disabled}
                        title={definition.title}
                        // The card itself may be pressable - picking a hardpoint aims at it - and
                        // the jump is a different act.
                        onClick={(event) => {
                            event.stopPropagation();
                            definition.onGo();
                        }}
                    >
                        <Icon name="definition" />
                    </button>
                )}
            </div>
            {type !== undefined && type !== null && (
                <span className="detail card-type">{type}</span>
            )}
            {children}
            {actions !== undefined && (
                <ViewRow className="card-actions">{actions}</ViewRow>
            )}
        </li>
    );
}
