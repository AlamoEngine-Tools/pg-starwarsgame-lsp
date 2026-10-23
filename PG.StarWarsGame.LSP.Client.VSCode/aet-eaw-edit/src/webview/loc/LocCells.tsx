// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The cell both localisation editors type into. The row menu they share is shared/RowMenu.

import { useEffect, useRef, useState } from "react";

/**
 * A cell that commits on blur or Enter and reverts on Escape.
 *
 * Local state while editing, so typing does not restage a command per keystroke - the queue would
 * fill with intermediate values and every render would rebuild the whole row list.
 */
export function CellInput(props: {
    value: string;
    onCommit: (next: string) => void;
}): React.JSX.Element {
    const [draft, setDraft] = useState(props.value);
    const committed = useRef(props.value);

    useEffect(() => {
        setDraft(props.value);
        committed.current = props.value;
    }, [props.value]);

    const commit = (): void => {
        if (draft !== committed.current) {
            committed.current = draft;
            props.onCommit(draft);
        }
    };

    return (
        <input
            type="text"
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            onBlur={commit}
            onKeyDown={(e) => {
                if (e.key === "Enter") {
                    e.currentTarget.blur();
                }
                if (e.key === "Escape") {
                    setDraft(committed.current);
                    e.currentTarget.blur();
                }
            }}
        />
    );
}
