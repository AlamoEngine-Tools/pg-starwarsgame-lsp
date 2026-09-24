// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import {IconButton} from './Button';
import {parseInteger, stepInteger} from './integerValue';

/**
 * A whole-number field: a themed text field between a decrement and an increment button.
 *
 * Not `<input type="number">`: its spinner ignores the editor theme, and it accepts 1.5 and 1e3.
 * The text is controlled by the caller, so a half-typed "-" survives until it is finished; the
 * field only marks it invalid (`parseInteger` decides).
 */
export function IntegerField(props: {
    value: string;
    onChange: (value: string) => void;
    ariaLabel?: string;
    disabled?: boolean;
    /** Why the field cannot be changed, shown on its buttons while disabled. */
    disabledReason?: string;
    autoFocus?: boolean;
}): React.JSX.Element {
    const valid = parseInteger(props.value) !== null;
    const reason = props.disabledReason ?? 'Read-only';
    return (
        <div className="integer-field">
            <IconButton icon="decrement" title="Decrease" disabled={props.disabled ?? false}
                        disabledReason={reason}
                        onClick={() => props.onChange(stepInteger(props.value, -1))}/>
            <input
                type="text" inputMode="numeric" value={props.value} disabled={props.disabled}
                aria-label={props.ariaLabel} aria-invalid={!valid} autoFocus={props.autoFocus}
                onChange={e => props.onChange(e.target.value)}
                onKeyDown={e => {
                    if (e.key === 'ArrowUp' || e.key === 'ArrowDown') {
                        e.preventDefault();
                        props.onChange(stepInteger(props.value, e.key === 'ArrowUp' ? 1 : -1));
                    }
                }}
            />
            <IconButton icon="add" title="Increase" disabled={props.disabled ?? false}
                        disabledReason={reason}
                        onClick={() => props.onChange(stepInteger(props.value, 1))}/>
        </div>
    );
}
