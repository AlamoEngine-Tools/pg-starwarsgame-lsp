// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import {useEffect, useId, useRef, useState} from 'react';

import {type ComboOption, isCurrentOption, moveHighlight, opensUpward, queryFor, visibleOptions} from './comboOptions';
import {Icon, type IconName} from './Icon';

/** The list's tallest extent, as `.suggest-list` caps it - what "room for the list" means. */
const LIST_MAX_HEIGHT = 160;

/**
 * A text field with a list of suggestions, any text accepted. Replaces the native `<datalist>`,
 * which filters by the field's own text from the moment it opens: a field holding a value then
 * offers only that value. Here the list opens with every option, the current one marked, and
 * filters only once the text is edited (`comboOptions.ts`).
 *
 * The options come from `options` (local) or `fetchOptions` (a server, asked for the
 * edited text and debounced; it answers already filtered). The text is controlled by the caller; `onPick` fires for an option taken from the
 * list, `onEnter` for Enter with no option highlighted, `onBlur` when focus leaves. Picking uses
 * `onMouseDown` + `preventDefault`, so the field never blurs half-way through a pick.
 */
export function Combobox(props: {
    value: string;
    onChange: (value: string) => void;
    options?: readonly ComboOption[];
    fetchOptions?: (query: string) => Promise<ComboOption[]>;
    onPick?: (value: string) => void;
    onEnter?: (value: string) => void;
    onBlur?: (value: string) => void;
    onFocus?: () => void;
    /** Escape with the list closed - the one after the Escape that closed it. */
    onEscape?: () => void;
    /** Select the text on focus, so typing replaces it - for a field showing a chosen label. */
    selectOnFocus?: boolean;
    placeholder?: string;
    /** Classes for the text field itself. */
    className?: string;
    disabled?: boolean;
    ariaLabel?: string;
    autoFocus?: boolean;
    /** A glyph inside the field's leading edge - `search` for a field that looks a name up. */
    icon?: IconName;
}): React.JSX.Element {
    const [open, setOpen] = useState(false);
    const [edited, setEdited] = useState(false);
    const [highlight, setHighlight] = useState(-1);
    const [fetched, setFetched] = useState<ComboOption[]>([]);
    const [upward, setUpward] = useState(false);
    const inputRef = useRef<HTMLInputElement>(null);
    const focused = useRef(false);
    const autoFocusPending = useRef(!!props.autoFocus);
    const fetchSeq = useRef(0);
    const debounce = useRef<number | undefined>(undefined);
    const listId = useId();
    useEffect(() => () => window.clearTimeout(debounce.current), []);

    const query = (text: string, wasEdited: boolean): void => {
        if (!props.fetchOptions) {
            return;
        }
        const seq = ++fetchSeq.current;
        window.clearTimeout(debounce.current);
        debounce.current = window.setTimeout(() => {
            void props.fetchOptions!(queryFor(text, wasEdited)).then(result => {
                // A reply to an older query, or one landing after focus left, is dropped.
                if (seq === fetchSeq.current && focused.current) {
                    setFetched(result);
                }
            });
        }, 150);
    };

    const shown = props.fetchOptions ? fetched : visibleOptions(props.options ?? [], props.value, edited);
    const expanded = open && !props.disabled && shown.length > 0;

    // Placed each time the list opens: a field at the foot of the dock drops its list upward.
    const placeList = (): void => {
        const rect = inputRef.current?.getBoundingClientRect();
        setUpward(!!rect && opensUpward(rect, window.innerHeight, LIST_MAX_HEIGHT));
    };

    const openList = (): void => {
        placeList();
        setOpen(true);
        setEdited(false);
        setHighlight(-1);
        query(props.value, false);
    };

    const pick = (value: string): void => {
        props.onChange(value);
        setOpen(false);
        setHighlight(-1);
        props.onPick?.(value);
    };

    return (
        <div className={props.icon ? 'suggest with-icon' : 'suggest'}>
            {props.icon ?
                <span className="suggest-icon" aria-hidden="true"><Icon name={props.icon} size={14}/></span> : null}
            <input
                ref={inputRef} type="text" role="combobox" className={props.className} value={props.value}
                disabled={props.disabled}
                placeholder={props.placeholder} aria-label={props.ariaLabel} autoFocus={props.autoFocus}
                aria-expanded={expanded} aria-controls={listId} aria-autocomplete="list"
                aria-activedescendant={expanded && highlight >= 0 ? `${listId}-${highlight}` : undefined}
                // Focus opens the list - except the focus a dialog gives its field on open, which
                // would drop the list over the dialog's own buttons. A press on a field that already
                // has focus reopens it; pointerdown rather than mousedown, because a host that
                // cancels pointerdown (the graph canvas does) suppresses the mouse events after it.
                onFocus={e => {
                    focused.current = true;
                    props.onFocus?.();
                    if (props.selectOnFocus) {
                        // Typing replaces the shown value rather than appending to it.
                        e.currentTarget.select();
                    }
                    if (autoFocusPending.current) {
                        autoFocusPending.current = false;
                    } else {
                        openList();
                    }
                }}
                onPointerDown={() => {
                    if (focused.current && !expanded) {
                        openList();
                    }
                }}
                onChange={e => {
                    props.onChange(e.target.value);
                    setEdited(true);
                    placeList();
                    setOpen(true);
                    setHighlight(-1);
                    query(e.target.value, true);
                }}
                onBlur={() => {
                    focused.current = false;
                    setOpen(false);
                    props.onBlur?.(props.value);
                }}
                onKeyDown={e => {
                    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
                        e.preventDefault();
                        if (!expanded) {
                            openList();
                            return;
                        }
                        setHighlight(h => moveHighlight(h, e.key === 'ArrowDown' ? 1 : -1, shown.length));
                    } else if (e.key === 'Escape') {
                        if (expanded) {
                            // Closing the list is all Escape does while it is open; a surrounding
                            // dialog only hears the next one.
                            e.stopPropagation();
                        } else {
                            props.onEscape?.();
                        }
                        setOpen(false);
                    } else if (e.key === 'Enter') {
                        if (expanded && highlight >= 0 && highlight < shown.length) {
                            e.preventDefault();
                            pick(shown[highlight].value);
                        } else {
                            setOpen(false);
                            props.onEnter?.(props.value);
                        }
                    }
                }}
            />
            {expanded ? (
                <div className={upward ? 'suggest-list up' : 'suggest-list'} role="listbox" id={listId}>
                    {shown.map((option, index) => {
                        const current = isCurrentOption(option, props.value);
                        return (
                            <div
                                key={option.value} id={`${listId}-${index}`} role="option"
                                aria-selected={current}
                                className={'suggest-item' + (current ? ' current' : '')
                                    + (index === highlight ? ' highlighted' : '')}
                                title={option.detail ?? undefined}
                                onMouseDown={e => {
                                    e.preventDefault(); // keep the field focused - no blur mid-pick
                                    pick(option.value);
                                }}
                            >{option.value}</div>
                        );
                    })}
                </div>
            ) : null}
        </div>
    );
}
