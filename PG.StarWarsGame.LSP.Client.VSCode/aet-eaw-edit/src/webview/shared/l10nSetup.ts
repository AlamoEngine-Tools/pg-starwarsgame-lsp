// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Configures `@vscode/l10n` from the bundle the extension host put on the page's root element - the
// same bundle VS Code loaded for the display language. Imported FIRST by every webview entry, so it
// runs before any other module's top-level code can ask for a text. English ships no bundle: the
// texts are the keys, and an empty bundle leaves them as written.

import * as l10n from "@vscode/l10n";

const raw = document.getElementById("root")?.dataset.l10n;
if (raw !== undefined && raw !== "") {
    const contents = JSON.parse(raw) as Record<string, string>;
    if (Object.keys(contents).length > 0) {
        l10n.config({ contents });
    }
}
