// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The localisation editors' search: the search plate, on the grid's lower right corner.
//
// It was a block at the foot of the dock - a field with three mode buttons, a "Search in" select,
// and in the translation editor a checkbox. It searches the grid, not anything in the dock, so it
// stands on the grid as every search stands on what it filters. The field holds the three ways of
// reading a pattern; which column the pattern is read in is a pill on the line above the field;
// the editor's own toggles sit beside the magnifier. Closing the field leaves all of it in force,
// and the filter icon is the reset.

import * as l10n from "@vscode/l10n";
import { type ReactNode, useState } from "react";

import { type FilterMode } from "../locFilter";
import { PillSelect } from "../shared/PillSelect";
import { SearchPlate } from "../shared/SearchPlate";

/** The scope a search starts in: every column. */
export const ALL_FIELDS = "all";

export interface LocSearchPlateProps {
    filter: string;
    onFilter: (value: string) => void;
    mode: FilterMode;
    onMode: (mode: FilterMode) => void;
    scope: string;
    onScope: (scope: string) => void;
    languages: string[];
    /** Why the pattern will not compile. */
    error?: string;
    /**
     * What the key column is called in this editor. It identifies an entry in a translation file
     * and is a formatting instruction in a credits file, so the pill must not call it the same
     * thing in both.
     */
    keyScopeLabel: string;
    /**
     * Example patterns for the field, per mode. Each editor supplies its own: a credits file's keys
     * are CENTER and HEADER, a translation file's are TEXT_*, and an example from the wrong one is
     * worse than none.
     */
    placeholders: Record<FilterMode, string>;
    /** Whether the editor's own toggles are away from where they rest. */
    toggled?: boolean;
    /** Puts the editor's own toggles back where they rest. The plate clears the rest itself. */
    onResetToggles?: () => void;
    /** The editor's own toggles - the Inherited switch, in translations. */
    children?: ReactNode;
}

export function LocSearchPlate(props: LocSearchPlateProps): React.JSX.Element {
    const [open, setOpen] = useState(false);

    const scopes = [
        { value: ALL_FIELDS, label: l10n.t("All fields") },
        { value: "key", label: props.keyScopeLabel },
        ...props.languages.map((language) => ({
            value: language,
            label: language,
        })),
    ];
    const scopeLabel =
        scopes.find((option) => option.value === props.scope)?.label ??
        props.scope;

    return (
        <SearchPlate
            open={open}
            pattern={props.filter}
            mode={props.mode}
            filtered={
                props.filter !== "" ||
                props.scope !== ALL_FIELDS ||
                (props.toggled ?? false)
            }
            error={props.error}
            placeholder={props.placeholders[props.mode]}
            title={l10n.t("Search the entries")}
            onPattern={props.onFilter}
            onMode={props.onMode}
            onOpen={setOpen}
            onReset={() => {
                props.onFilter("");
                props.onScope(ALL_FIELDS);
                props.onResetToggles?.();
            }}
            pills={
                <PillSelect
                    icon="searchScope"
                    className={props.scope === ALL_FIELDS ? undefined : "set"}
                    value={props.scope}
                    shown={scopeLabel}
                    options={scopes}
                    title={l10n.t("Search in - {0}", scopeLabel)}
                    onPick={props.onScope}
                />
            }
        >
            {props.children}
        </SearchPlate>
    );
}
