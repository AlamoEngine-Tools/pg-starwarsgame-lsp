// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import {useState} from 'react';

import {Combobox} from './Combobox';
import {resolveChoice} from './comboOptions';
import type {IconName} from './Icon';

/**
 * The smart field for a value that must be one of its options - a filter, an enum - in place of a
 * native `<select>`. It shows the chosen option by its label; typing searches the options; a pick,
 * or Enter on the one option the text names, commits it. Anything else reverts to the chosen label
 * on Enter, Escape or blur: typed text is a search, never a value.
 */
export function SelectField(props: {
    value: string;
    /** `label` is what the field shows; an option without one shows its value. */
    options: readonly { value: string; label?: string }[];
    onChange: (value: string) => void;
    ariaLabel: string;
    title?: string;
    icon?: IconName;
    disabled?: boolean;
}): React.JSX.Element {
    const labelOf = (value: string): string => props.options.find(o => o.value === value)?.label ?? value;
    // The text being typed, or null while the field shows the chosen option.
    const [draft, setDraft] = useState<string | null>(null);
    const shown = props.options.map(o => ({value: o.label ?? o.value}));

    const commit = (label: string | null): void => {
        setDraft(null);
        const option = label === null ? undefined : props.options.find(o => (o.label ?? o.value) === label);
        if (option && option.value !== props.value) {
            props.onChange(option.value);
        }
    };

    return (
        <span className="select-field" title={props.title}>
            <Combobox
                value={draft ?? labelOf(props.value)}
                options={shown}
                ariaLabel={props.ariaLabel}
                icon={props.icon}
                disabled={props.disabled}
                selectOnFocus
                onChange={setDraft}
                onPick={commit}
                onEnter={text => commit(resolveChoice(shown, text))}
                onEscape={() => setDraft(null)}
                onBlur={() => setDraft(null)}
            />
        </span>
    );
}
