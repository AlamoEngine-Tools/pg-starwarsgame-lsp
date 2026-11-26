// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The Model tree: every bone, mesh and proxy, filtered, folded and switched row by row.

import type { Dispatch, SetStateAction } from "react";
import type { ModelTree } from "../useModelTree";
import * as l10n from "@vscode/l10n";
import { IconButton } from "../../shared/Button";
import { SearchPlate } from "../../shared/SearchPlate";
import { DockSection } from "../../shared/DockSection";
import {
    EYE_ICONS,
    KIND_ICONS,
    KIND_LABELS,
    TREE_KINDS,
} from "../previewLabels";
import { Icon } from "../../shared/Icon";
import { TreeRow } from "../../shared/TreeRow";
import { selectionAfterClick } from "../previewTree";
import { becauseText } from "../visibility";
import { rowEye } from "../rowEye";
import { treeMinHeight } from "../treeSearch";

export interface ModelTreeSectionProps {
    anchor: string | null;
    clearSelection: () => void;
    detailsRow: string | null;
    openInspector: (rowId: string) => void;
    selected: ReadonlySet<string>;
    setAnchor: Dispatch<SetStateAction<string | null>>;
    setSearchPressed: Dispatch<SetStateAction<boolean>>;
    setSelected: Dispatch<SetStateAction<ReadonlySet<string>>>;
    tree: ModelTree;
}

export function ModelTreeSection({
    anchor,
    clearSelection,
    detailsRow,
    openInspector,
    selected,
    setAnchor,
    setSearchPressed,
    setSelected,
    tree,
}: ModelTreeSectionProps): React.JSX.Element {
    return (
        <DockSection
            title={l10n.t("Model tree")}
            end={
                <>
                    {/* The way OUT of a selection. Escape has always cleared
                                        it and clicking the row again does now, but neither is
                                        visible - the reader's complaint was that there was no
                                        obvious way back, and a count that does nothing was the
                                        only thing on screen saying a selection existed. */}
                    {selected.size > 0 && (
                        <button
                            className="section-count as-button"
                            title={l10n.t("Clear the selection")}
                            onClick={clearSelection}
                        >
                            {l10n.t("{0} selected", selected.size)}
                            <Icon name="close" size={11} />
                        </button>
                    )}

                    {/* BOTH, when both apply. The selection readout used to
                                        replace the count outright, which put an active filter
                                        back out of sight the moment a row was picked - and
                                        hiding a filter is the one thing this header exists to
                                        stop. They are separate facts about the tree. */}
                    <span
                        className="section-count"
                        title={
                            tree.filterSummary === null
                                ? undefined
                                : l10n.t("Some rows are filtered out")
                        }
                    >
                        {tree.filterSummary ?? tree.treeRows.length}
                    </span>

                    {/* Reset stands with the count because they are the same
                                        corner of the same header: one says what state the tree
                                        is in, the other is the way out of it. Below the list it
                                        was a row of its own, a hundred rows from what it
                                        undoes. */}
                    <IconButton
                        icon="reset"
                        className="header-right"
                        size={13}
                        title={l10n.t("Put the model back the way it opened")}
                        disabled={!tree.touched}
                        disabledReason={l10n.t("The model is as it opened")}
                        onClick={tree.resetModel}
                    />
                </>
            }
        >
            {/* The list and the search element share a box, because the second
                                    one is drawn ON the first. Positioned against this rather than
                                    against the section, so the strip lands inside the tree's own
                                    bounds and not over the skeleton control below it. */}
            <div className="tree-pane">
                <ul
                    className="bone-tree"
                    style={{
                        minHeight: treeMinHeight(tree.unfilteredRowCount),
                    }}
                >
                    {tree.treeRows.map(({ node, expandable, expanded }) => (
                        <TreeRow
                            key={node.id}
                            id={node.id}
                            kind={node.kind}
                            name={node.name}
                            depth={node.depth}
                            expandable={expandable}
                            expanded={expanded}
                            onToggle={() => tree.toggleCollapsed(node.id)}
                            className={
                                "bone-row" +
                                (selected.has(node.id) ? " selected" : "") +
                                (node.gatedOff ? " hidden-bone" : "") +
                                (node.scaffold === true ? " scaffold" : "")
                            }
                            title={
                                node.because === undefined
                                    ? node.name
                                    : `${node.name}
${becauseText(node.because)}`
                            }
                            onClick={(event) => {
                                // Clicking the row that is ALREADY the whole
                                // selection clears it. There was no other way out
                                // of a selection at all - the box and the axes
                                // stayed until you picked something else.
                                const plain =
                                    !(event.ctrlKey || event.metaKey) &&
                                    !event.shiftKey;

                                if (
                                    plain &&
                                    selected.size === 1 &&
                                    selected.has(node.id)
                                ) {
                                    clearSelection();
                                    return;
                                }

                                const after = selectionAfterClick(
                                    tree.treeRows,
                                    selected,
                                    anchor,
                                    node.id,
                                    {
                                        ctrl: event.ctrlKey || event.metaKey,
                                        shift: event.shiftKey,
                                    },
                                );
                                setSelected(after.selected);
                                setAnchor(after.anchor);
                            }}
                        >
                            {/* Blender's arrangement, and the reader asked for it
                                                by name: an open eye, a half-shut one for a row
                                                whose visibility is inherited from above, and a
                                                closed one for a row that is off. A tick could only
                                                ever say on or off, which is one bit short - it
                                                could not tell "the file hides this, press to show
                                                it" from "a bone above it is hidden, pressing does
                                                nothing", and the second one made the control look
                                                broken. See `rowEye.ts`. */}
                            {(() => {
                                const eye = rowEye({
                                    visible: node.visible,
                                    decidedBy: node.decidedBy ?? "file",
                                    authored: !node.gatedOff,
                                });

                                return (
                                    <IconButton
                                        icon={EYE_ICONS[eye.state]}
                                        className={`row-eye eye-${eye.state}`}
                                        size={13}
                                        pressed={eye.state === "shown"}
                                        title={eye.title}
                                        disabled={!eye.canAct}
                                        // Already the reason when it cannot act:
                                        // rowEye phrases the title from the state,
                                        // and a row that cannot be hidden says so.
                                        disabledReason={eye.title}
                                        onClick={(event) => {
                                            event.stopPropagation();
                                            tree.setRowsVisible(
                                                node.id,
                                                !node.visible,
                                            );
                                        }}
                                    />
                                );
                            })()}

                            {/* Dim until the row is under the pointer or already
                                                open, so a deep tree stays a list of names rather
                                                than a column of buttons - but always THERE, since
                                                a control that only exists on hover is one nobody
                                                finds. */}
                            <IconButton
                                icon="details"
                                className={
                                    "row-details" +
                                    (detailsRow === node.id ? " active" : "")
                                }
                                size={13}
                                // pressed, not expanded: it opens an editor tab
                                // beside this one, which is not this control
                                // disclosing something underneath it.
                                pressed={detailsRow === node.id}
                                title={l10n.t("Inspect {0}", node.name)}
                                onClick={(event) => {
                                    event.stopPropagation();
                                    openInspector(node.id);
                                }}
                            />
                        </TreeRow>
                    ))}
                    {/* Inside the list, not after it. Below the box it was one
                                        more thing appearing where rows used to be, so the panel
                                        still moved - just by the height of the message instead of
                                        the height of the tree. */}
                    {tree.treeRows.length === 0 && (
                        <li className="dock-hint tree-empty">
                            {l10n.t("Nothing matches that filter.")}
                        </li>
                    )}
                </ul>
                {/* Show and Hide used to sit here, doing what the row's own tick
                                    does - and doing it to whatever happened to be selected, which
                                    is a second way to say the same thing and a second thing to get
                                    wrong. What the panel actually lacked was a way BACK: after
                                    twenty ticks, a damage level and a detail level, nothing said
                                    what the model looked like when it was opened. */}

                {/* ONE element, floating on the tree's bottom edge.

                                    The pattern and the kinds are the same question - which rows do
                                    I want - and they stood as two unrelated blocks with a gap
                                    between them, the input full width and the icons adrift
                                    beneath it. A mesh origin IS a bone and an emitter attaches to
                                    one, so all three kinds belong here rather than split across
                                    panels.

                                    ON the tree rather than under it, so it costs the list no
                                    height at all. The list carries a matching pad at its foot, so
                                    the last row can still be scrolled clear of the strip. */}
                <SearchPlate
                    open={tree.searchOpen}
                    pattern={tree.boneFilter}
                    mode={tree.boneFilterMode}
                    filtered={tree.filtered}
                    error={tree.filterError}
                    placeholder={l10n.t("Filter the tree, e.g. HP_")}
                    title={l10n.t("Filter the tree by name")}
                    onPattern={tree.setBoneFilter}
                    onMode={tree.setBoneFilterMode}
                    onOpen={setSearchPressed}
                    onReset={tree.resetFilter}
                    beside
                >
                    {/* Always here, open or closed. A pattern and a kind are two
                                        halves of the same question, and hiding one of them the
                                        moment you start typing makes narrowing by kind a round
                                        trip out of the box you are typing in. */}
                    {TREE_KINDS.map((kind) => (
                        <IconButton
                            key={kind}
                            icon={KIND_ICONS[kind]}
                            className={
                                tree.kinds.has(kind) ? "active" : undefined
                            }
                            pressed={tree.kinds.has(kind)}
                            title={l10n.t(
                                "Show {0}",
                                KIND_LABELS[kind].toLowerCase(),
                            )}
                            onClick={() =>
                                tree.setKinds((current) => {
                                    const next = new Set(current);
                                    if (!next.delete(kind)) {
                                        next.add(kind);
                                    }
                                    return next;
                                })
                            }
                        />
                    ))}
                </SearchPlate>
            </div>
        </DockSection>
    );
}
