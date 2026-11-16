// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The credits crawl as its own panel, opened beside the editor the way a Markdown or LaTeX preview
// is.
//
// It shows what the editor has *staged*, not what is on disk - a reordered or newly inserted line
// appears before you save, which is the whole point of previewing an ordered format. The editor
// pushes its rows here whenever they change.

import "./shared/l10nSetup";
import * as l10n from "@vscode/l10n";
import { useEffect, useState } from "react";
import { createRoot } from "react-dom/client";

import { CreditsCrawl } from "./creditsCrawl";
import { LocRow } from "./loc/locRow";
import type {
    CreditsPreviewToHost,
    CreditsPreviewToWebview,
} from "../protocol/panels/creditsPreview";
import { poster, transport } from "./shared/vscodeApi";

const post = poster<CreditsPreviewToHost>();

interface CrawlPayload {
    rows: LocRow[];
    languages: string[];
    language: string;
}

function App(): React.JSX.Element {
    const [payload, setPayload] = useState<CrawlPayload | null>(null);

    useEffect(() => {
        const stop = transport.listen((data) => {
            const msg = data as CreditsPreviewToWebview;
            if (msg.type !== "crawl") {
                return;
            }
            setPayload({
                rows: msg.rows,
                languages: msg.languages,
                language: msg.language,
            });
        });
        post({ type: "ready" });
        return stop;
    }, []);

    if (payload === null) {
        return (
            <p style={{ padding: 12, opacity: 0.7 }}>
                {l10n.t("Waiting for the editor...")}
            </p>
        );
    }

    return (
        <CreditsCrawl
            rows={payload.rows}
            languages={payload.languages}
            initialLanguage={payload.language}
            // Closing the crawl closes the panel: it is the only thing here.
            onClose={() => post({ type: "close" })}
        />
    );
}

createRoot(document.getElementById("root")!).render(<App />);
