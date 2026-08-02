// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The credits crawl as its own panel, opened beside the editor the way a Markdown or LaTeX preview
// is.
//
// It shows what the editor has *staged*, not what is on disk - a reordered or newly inserted line
// appears before you save, which is the whole point of previewing an ordered format. The editor
// pushes its rows here whenever they change.

import { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';

import { CreditsCrawl } from './creditsCrawl';
import { LocRow } from './loc/locRow';

declare function acquireVsCodeApi(): { postMessage(message: unknown): void };
const vscode = acquireVsCodeApi();

interface CrawlPayload {
    rows: LocRow[];
    languages: string[];
    language: string;
}

function App(): React.JSX.Element {
    const [payload, setPayload] = useState<CrawlPayload | null>(null);

    useEffect(() => {
        const handle = (event: MessageEvent): void => {
            const msg = event.data as { type: string; [key: string]: unknown };
            if (msg.type !== 'crawl') { return; }
            setPayload({
                rows: msg.rows as LocRow[],
                languages: msg.languages as string[],
                language: msg.language as string,
            });
        };

        window.addEventListener('message', handle);
        vscode.postMessage({ type: 'ready' });
        return () => window.removeEventListener('message', handle);
    }, []);

    if (payload === null) {
        return <p style={{ padding: 12, opacity: 0.7 }}>Waiting for the editor...</p>;
    }

    return (
        <CreditsCrawl
            rows={payload.rows}
            languages={payload.languages}
            initialLanguage={payload.language}
            // Closing the crawl closes the panel: it is the only thing here.
            onClose={() => vscode.postMessage({ type: 'close' })}
        />
    );
}

createRoot(document.getElementById('root')!).render(<App />);
