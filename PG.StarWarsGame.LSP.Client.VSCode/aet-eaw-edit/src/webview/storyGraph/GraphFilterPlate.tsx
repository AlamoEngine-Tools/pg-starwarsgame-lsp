// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The story graph's event filter: the search plate, on the canvas's lower right corner.
//
// It was a field and three selects in the dock's foot, with a clear button among the view tools.
// It filters the canvas, so it stands on the canvas as every search stands on what it filters. The
// field holds the three ways of reading a pattern, which the server applies; the branch, the
// lifecycle and the plot state are pills on the line above the field; the filter icon clears all
// four. Closing the field leaves them in force.

import * as l10n from "@vscode/l10n";
import { useState } from "react";

import { PillSelect } from "../shared/PillSelect";
import { SearchPlate } from "../shared/SearchPlate";
import { compileMatcher, type SearchMode } from "../shared/searchPattern";
import { anyFilterSet } from "./graphFilters";

/** The filters the plate sets, as the graph view holds them. */
export interface GraphFilterValues {
    nameFilter: string;
    nameFilterMode: SearchMode;
    branch: string;
    lifecycle: string;
    plotState: string;
    reachableFrom: string;
}

export interface GraphFilterPlateProps {
    filters: GraphFilterValues;
    /**
     * The pattern as typed. The field shows it at once; only the APPLIED filter waits for typing to
     * settle - each one costs a server request and a full rebuild (#131).
     */
    draft: string;
    onDraft: (draft: string) => void;
    onFilter: (patch: Partial<GraphFilterValues>) => void;
    /** Clears every filter but the mode. */
    onReset: () => void;
    branches: readonly string[];
    lifecycles: readonly string[];
    plotStates: readonly string[];
}

export function GraphFilterPlate(
    props: GraphFilterPlateProps,
): React.JSX.Element {
    const [open, setOpen] = useState(false);
    const { filters } = props;

    // The draft rather than the applied filter, so the reset wakes with the first letter rather
    // than with the fetch it is waiting on.
    const filtered = anyFilterSet({ ...filters, nameFilter: props.draft });

    const pill = (
        value: string,
        icon: "branch" | "lifecycle" | "plotState",
        any: string,
        values: readonly string[],
        name: string,
        pick: (value: string) => void,
    ): React.JSX.Element => (
        <PillSelect
            icon={icon}
            className={value === "" ? undefined : "set"}
            value={value}
            shown={value === "" ? any : value}
            options={[
                { value: "", label: any },
                ...values.map((v) => ({ value: v, label: v })),
            ]}
            title={l10n.t("{0} - {1}", name, value === "" ? any : value)}
            onPick={pick}
        />
    );

    return (
        <SearchPlate
            open={open}
            pattern={props.draft}
            mode={filters.nameFilterMode}
            filtered={filtered}
            // Compiled here as well as on the server, to say why a regular expression finds nothing.
            error={compileMatcher(props.draft, filters.nameFilterMode).error}
            placeholder={l10n.t("Filter event names")}
            onPattern={props.onDraft}
            onMode={(nameFilterMode) => props.onFilter({ nameFilterMode })}
            onOpen={setOpen}
            onReset={props.onReset}
            variant="placed"
            pills={
                <>
                    {pill(
                        filters.branch,
                        "branch",
                        l10n.t("All branches"),
                        props.branches,
                        l10n.t("Branch"),
                        (branch) => props.onFilter({ branch }),
                    )}
                    {pill(
                        filters.lifecycle,
                        "lifecycle",
                        l10n.t("Any lifecycle"),
                        props.lifecycles,
                        l10n.t("Lifecycle"),
                        (lifecycle) => props.onFilter({ lifecycle }),
                    )}
                    {pill(
                        filters.plotState,
                        "plotState",
                        l10n.t("Any plot state"),
                        props.plotStates,
                        l10n.t("Plot state"),
                        (plotState) => props.onFilter({ plotState }),
                    )}
                </>
            }
        />
    );
}
