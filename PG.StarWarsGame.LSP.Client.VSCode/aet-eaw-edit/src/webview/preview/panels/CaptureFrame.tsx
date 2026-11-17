// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The area a capture writes: the largest box of the capture's shape that fits, centred, with the
// rest dimmed - the same arithmetic the capture renders with (captureFrame).

import * as l10n from "@vscode/l10n";

export interface CaptureFrameProps {
    captureDims: { width: number; height: number };
}

export function CaptureFrame({
    captureDims,
}: CaptureFrameProps): React.JSX.Element {
    return (
        <div className="capture-frame-host" aria-hidden="true">
            <div
                className="capture-frame"
                style={{
                    ["--capture-ratio" as string]: String(
                        captureDims.width / captureDims.height,
                    ),
                }}
            >
                <span className="capture-frame-size">
                    {l10n.t(
                        "{0} x{1}{2}",
                        captureDims.width,
                        " ",
                        captureDims.height,
                    )}
                </span>
            </div>
        </div>
    );
}
