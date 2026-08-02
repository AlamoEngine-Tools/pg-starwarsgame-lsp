// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The dock's search block, shared by both editors.
//
// It was three stacked rows - a box, a strip of mode buttons, a scope picker - in six pixels of
// padding at the foot of the dock, which read as leftovers rather than as one control. The mode
// toggles now sit inside the box the way an editor's find widget puts them, which buys back a row,
// and what is left is a labelled field with room to breathe.

import { ReactNode } from 'react';

import { FilterMode } from '../locFilter';

export interface LocSearchProps {
    filter: string;
    onFilter: (value: string) => void;
    mode: FilterMode;
    onMode: (mode: FilterMode) => void;
    scope: string;
    onScope: (scope: string) => void;
    languages: string[];
    /** Why the pattern will not compile, shown under the box. */
    error?: string;
    /**
     * What the key column is called in this editor. It identifies an entry in a translation file
     * and is a formatting instruction in a credits file, so the picker must not call it the same
     * thing in both.
     */
    keyScopeLabel: string;
    /**
     * Example patterns for the box, per mode. Each editor supplies its own: a credits file's keys
     * are CENTER and HEADER, a translation file's are TEXT_*, and an example from the wrong one is
     * worse than none.
     */
    placeholders: Record<FilterMode, string>;
    /** Editor-specific filters - the inherited toggle, in translations. */
    children?: ReactNode;
}

// The icons and wording the sidebar shipped with - unchanged, because users have them in their
// fingers and this is a layout change, not a behaviour one.
const MODES: { mode: FilterMode; icon: string; title: string }[] = [
    { mode: 'text', icon: 'case-sensitive', title: 'Plain text search' },
    {
        mode: 'wildcard', icon: 'star-full',
        title: 'Wildcard: * matches any text, ? matches one character',
    },
    { mode: 'regex', icon: 'regex', title: 'Regular expression (case-insensitive)' },
];

export function LocSearch(props: LocSearchProps): React.JSX.Element {
    return (
        <div className="dock-search">
            <div className="dock-section-title">Search</div>

            <div className="search-field with-modes">
                <input
                    type="text"
                    className={`filter${props.error ? ' invalid' : ''}`}
                    placeholder={props.placeholders[props.mode]}
                    value={props.filter}
                    onChange={e => props.onFilter(e.target.value)}
                    aria-invalid={props.error !== undefined}
                />
                <div className="mode-group">
                    {MODES.map(m => (
                        <button
                            key={m.mode}
                            className={`icon-btn${props.mode === m.mode ? ' active' : ''}`}
                            title={m.title}
                            aria-label={m.mode}
                            aria-pressed={props.mode === m.mode}
                            onClick={() => props.onMode(m.mode)}
                        >
                            <span className={`codicon codicon-${m.icon}`} />
                        </button>
                    ))}
                </div>
            </div>

            {/* Without this the grid just empties, which reads as "no matches" rather than "that
                pattern is not valid" - the two are identical on screen. */}
            {props.error && <div className="filter-error">{props.error}</div>}

            <label className="field">
                <span className="field-label">Search in</span>
                <select value={props.scope} onChange={e => props.onScope(e.target.value)}>
                    <option value="all">All fields</option>
                    <option value="key">{props.keyScopeLabel}</option>
                    {props.languages.map(language => (
                        <option key={language} value={language}>{language}</option>
                    ))}
                </select>
            </label>

            {props.children}
        </div>
    );
}
