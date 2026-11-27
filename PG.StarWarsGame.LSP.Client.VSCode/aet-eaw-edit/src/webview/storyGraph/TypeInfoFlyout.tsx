// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The documentation of one event or reward type, beside the palette: what the type does, what the
// schema notes about it, and what each of its parameters holds.
//
// A palette tile has room for a name. This is the rest, one press on the tile's info pill away and
// drawn BESIDE the dock - never unfolded inside the palette, which would push two hundred tiles
// down to make room for one. The shell is the shared details flyout, the one a hardpoint card's
// info pill opens in the model preview, tail included.

import * as l10n from "@vscode/l10n";
import { useEffect, useRef } from "react";

import type { StoryParamSchemaDto } from "../../protocol";
import { badgeIcon } from "../shared/badges";
import { DetailsFlyout } from "../shared/DetailsFlyout";
import { Icon } from "../shared/Icon";
import { useFlyoutBesideDock } from "../shared/useFlyoutBesideDock";
import { paramLabel } from "./paramLabels";
import { noteLine, noteSeverity, paramKind, type TypeDoc } from "./typeDocs";
import { otherName, tileName, type TypeNames } from "./typeNames";

/** Which type's documentation is open: the type, and whether it is an event or a reward. */
export interface TypeInfo {
    category: "trigger" | "reward";
    type: string;
}

/** The info pill of a type's tile, which is what the flyout opens at. */
export function typeInfoSelector(info: TypeInfo): string {
    return `.palette-tile[data-category="${info.category}"][data-type="${CSS.escape(info.type)}"] .tile-info`;
}

export interface TypeInfoFlyoutProps {
    info: TypeInfo;
    names: TypeNames;
    doc: TypeDoc | undefined;
    params: readonly StoryParamSchemaDto[];
    onClose: () => void;
}

export function TypeInfoFlyout({
    info,
    names,
    doc,
    params,
    onClose,
}: TypeInfoFlyoutProps): React.JSX.Element {
    const box = useRef<HTMLDivElement | null>(null);
    const at = useFlyoutBesideDock(typeInfoSelector(info), box);

    // Escape closes it, like every flyout: it is opened by a press and should not need the pill
    // found again to go away.
    useEffect(() => {
        const onKey = (event: KeyboardEvent): void => {
            if (event.key === "Escape") {
                onClose();
            }
        };

        document.addEventListener("keydown", onKey);
        return () => document.removeEventListener("keydown", onKey);
    }, [onClose]);

    // The name the tiles show, then the other one: the flyout is where both are read in full.
    const shown = tileName(names, info.type, "").text;
    const other = otherName(names, info.type, "");
    const slots = [...params].sort((a, b) => a.position - b.position);

    return (
        <DetailsFlyout
            className="type-doc"
            title={shown}
            subtitle={other ?? undefined}
            at={at}
            ref={box}
            onClose={onClose}
        >
            {doc?.description ? (
                <p className="type-doc-text">{doc.description}</p>
            ) : null}

            {(doc?.notes ?? []).length > 0 ? (
                <ul className="type-doc-notes">
                    {(doc?.notes ?? []).map((note, index) => (
                        <li key={index} className={`sev-${noteSeverity(note)}`}>
                            <Icon
                                name={badgeIcon(noteSeverity(note))}
                                size={14}
                            />
                            <span>{noteLine(note)}</span>
                        </li>
                    ))}
                </ul>
            ) : null}

            <div className="type-doc-heading">
                {l10n.t("Parameters")}
                <span className="section-count">{slots.length}</span>
            </div>
            {slots.length === 0 ? (
                <p className="type-doc-none">{l10n.t("No parameters")}</p>
            ) : (
                <ol className="type-doc-params">
                    {slots.map((param) => (
                        <li key={param.position}>
                            <span className="param-position">
                                {param.position + 1}
                            </span>
                            <span className="param-name">
                                {paramLabel(
                                    param,
                                    l10n.t("Param {0}", param.position + 1),
                                )}
                            </span>
                            <span className="param-kind">
                                {paramKind(param)}
                            </span>
                            {param.optional ? (
                                <span className="param-optional">
                                    {l10n.t("Optional")}
                                </span>
                            ) : null}
                            {param.description ? (
                                <p className="param-text">
                                    {param.description}
                                </p>
                            ) : null}
                        </li>
                    ))}
                </ol>
            )}
        </DetailsFlyout>
    );
}
