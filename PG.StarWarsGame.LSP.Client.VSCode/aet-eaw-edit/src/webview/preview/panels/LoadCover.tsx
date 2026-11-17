// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The cover over the canvas while the subject assembles itself, with the counts of what is left.

import { type LoadState } from "../loadProgress";

export interface LoadCoverProps {
    loading: LoadState;
}

export function LoadCover({ loading }: LoadCoverProps): React.JSX.Element {
    return (
        <div className="load-cover" role="status" aria-live="polite">
            <div className="load-spinner" />
            <span className="load-label">{loading.label}</span>
            {loading.detail !== null && (
                <span className="load-detail">{loading.detail}</span>
            )}
        </div>
    );
}
