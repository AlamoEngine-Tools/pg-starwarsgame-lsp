// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The What's New page. The host hands over the installed version's changelog section as
// Markdown; this renders it as a document in the editor's theme, the highlights first, the
// migration guide as a callout, the earlier releases folded below.
//
// The Markdown is our own changelog, shipped inside the VSIX, but the page still renders it with
// raw HTML switched off and runs the result through DOMPurify before it reaches the DOM: a webview
// is a privileged context, and the cost of the guard is nothing.

import "./shared/l10nSetup";
import * as l10n from "@vscode/l10n";
import DOMPurify from "dompurify";
import MarkdownIt from "markdown-it";
import { useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import styled from "styled-components";

import { tokensCss } from "./shared/tokens";
import { DISCUSSIONS_URL, ISSUES_URL, releaseUrl } from "./whatsNewLinks";

declare function acquireVsCodeApi(): { postMessage(message: unknown): void };

const vscode = acquireVsCodeApi();

interface Section {
    version: string;
    highlights?: string;
    migration?: string;
    body: string;
}

interface Payload {
    type: "notes";
    version: string;
    preview: boolean;
    current?: Section;
    older: { version: string; markdown: string }[];
}

const markdown = new MarkdownIt({
    html: false,
    linkify: false,
    typographer: false,
});

/**
 * Markdown to HTML the DOM may take: no raw HTML in, DOMPurify over what comes out, last.
 *
 * A setting key in code (`aet-eaw-edit.lsp.debug.trace.server`) may break after a dot, where it
 * still reads as one key, and never at a hyphen, where `aet-eaw-` on one line and `edit...` on the
 * next reads as two. The break opportunities are written in as `<wbr>` and the code is otherwise
 * kept on one line.
 */
function render(text: string): string {
    const html = markdown
        .render(text)
        .replace(
            /<code>([^<]*)<\/code>/g,
            (_whole, code: string) =>
                `<code>${code.replace(/\./g, ".<wbr>")}</code>`,
        );
    return DOMPurify.sanitize(html, {
        USE_PROFILES: { html: true },
    });
}

const Shell = styled.div`
    ${tokensCss}
    min-height: 100%;
    padding: var(--space-24) calc(var(--space-24) + var(--space-8));
    font-family: var(--vscode-font-family);
    font-size: calc(var(--vscode-font-size) * 1.05);
    line-height: 1.55;
    color: var(--colour-editor-ink);
    background: var(--colour-editor-ground);

    .page {
        max-width: 78ch;
        margin: 0 auto;
    }

    .masthead {
        display: flex;
        flex-wrap: wrap;
        align-items: baseline;
        gap: var(--space-8) var(--space-16);
        padding-bottom: var(--space-12);
        border-bottom: 1px solid var(--colour-line);
        margin-bottom: var(--space-24);
    }

    .masthead h1 {
        font-size: 1.7em;
        font-weight: 600;
        margin: 0;
        flex: 1 1 auto;
    }

    .masthead h1 .preview {
        display: inline-block;
        vertical-align: middle;
        margin-left: var(--space-12);
        padding: 0 var(--space-8);
        border-radius: var(--radius-pill);
        font-size: 0.5em;
        font-weight: 600;
        line-height: 1.8;
        letter-spacing: 0.04em;
        text-transform: uppercase;
        color: var(--vscode-badge-foreground, #ffffff);
        background: var(--vscode-badge-background, #616161);
        cursor: default;
    }

    .masthead nav {
        display: flex;
        gap: var(--space-16);
    }

    a {
        color: var(--colour-heading);
        text-decoration: none;
    }

    a:hover {
        color: var(--colour-heading-hover);
        text-decoration: underline;
    }

    .notes h3 {
        font-size: 1.25em;
        font-weight: 600;
        margin: calc(var(--space-24) + var(--space-8)) 0 var(--space-12);
        padding-bottom: var(--space-4);
        border-bottom: 1px solid var(--colour-line);
    }

    .notes h4 {
        font-size: 1.05em;
        margin: var(--space-16) 0 var(--space-8);
    }

    .notes p,
    .notes ul,
    .notes table {
        margin: 0 0 var(--space-12);
    }

    .notes ul {
        padding-left: var(--space-24);
    }

    .notes li {
        margin: var(--space-4) 0;
    }

    .notes li > ul {
        margin: var(--space-4) 0 0;
    }

    .notes code {
        font-family: var(--vscode-editor-font-family);
        font-size: 0.92em;
        padding: 0 var(--space-4);
        border-radius: var(--radius-3);
        background: var(--vscode-textCodeBlock-background);
    }

    .notes pre {
        padding: var(--space-12);
        border-radius: var(--radius-3);
        background: var(--vscode-textCodeBlock-background);
        overflow: auto;
    }

    .notes pre code {
        padding: 0;
        background: none;
    }

    .notes table {
        border-collapse: collapse;
        display: block;
        max-width: 100%;
        overflow-x: auto;
    }

    .notes th,
    .notes td {
        padding: var(--space-4) var(--space-8);
        border: 1px solid var(--colour-line);
        text-align: left;
    }

    .notes th {
        background: var(--colour-panel);
    }

    /* See render(): a key breaks after a dot, where <wbr> allows it, never at a hyphen. */
    .notes code {
        white-space: nowrap;
    }

    .notes img {
        display: block;
        max-width: 100%;
        margin: var(--space-12) 0;
        border: 1px solid var(--colour-line);
        border-radius: var(--radius-3);
    }

    .highlights {
        font-size: 1.08em;
    }

    .callout {
        margin: var(--space-24) 0;
        padding: var(--space-12) var(--space-16);
        border-left: 3px solid var(--colour-heading);
        border-radius: 0 var(--radius-3) var(--radius-3) 0;
        background: var(--colour-panel);
    }

    .callout h2 {
        font-size: 1.1em;
        font-weight: 600;
        margin: 0 0 var(--space-8);
    }

    .older {
        margin-top: calc(var(--space-24) + var(--space-8));
        padding-top: var(--space-12);
        border-top: 1px solid var(--colour-line);
    }

    .older h2 {
        font-size: 0.9em;
        font-weight: 600;
        text-transform: uppercase;
        letter-spacing: 0.04em;
        color: var(--colour-muted);
        margin: 0 0 var(--space-8);
    }

    details {
        margin: var(--space-4) 0;
    }

    summary {
        cursor: pointer;
        padding: var(--space-4) 0;
        font-weight: 600;
        color: var(--colour-heading);
    }

    .loading {
        color: var(--colour-muted);
    }
`;

/** Rendered Markdown. The HTML has been through DOMPurify in render(). */
function Notes({
    text,
    className,
}: {
    text: string;
    className?: string;
}): React.JSX.Element {
    return (
        <div
            className={className === undefined ? "notes" : `notes ${className}`}
            dangerouslySetInnerHTML={{ __html: render(text) }}
        />
    );
}

function App(): React.JSX.Element {
    const [payload, setPayload] = useState<Payload | null>(null);

    useEffect(() => {
        const handle = (event: MessageEvent): void => {
            const msg = event.data as { type?: unknown };
            if (msg.type === "notes") {
                setPayload(event.data as Payload);
            }
        };
        window.addEventListener("message", handle);
        vscode.postMessage({ type: "ready" });
        return () => window.removeEventListener("message", handle);
    }, []);

    if (payload === null) {
        return (
            <Shell>
                <div className="page">
                    <p className="loading">
                        {l10n.t("Loading the release notes...")}
                    </p>
                </div>
            </Shell>
        );
    }

    const { current, older, version, preview } = payload;
    return (
        // Links need no handler: the webview host opens every web link in the browser itself,
        // and a handler of our own opened each one a second time.
        <Shell>
            <div className="page">
                <header className="masthead">
                    <h1>
                        {l10n.t("What's New in EaWEdit {0}", version)}
                        {preview && (
                            <span
                                className="preview"
                                title={l10n.t(
                                    "Preview release - behaviour may change between versions",
                                )}
                            >
                                {l10n.t("Preview")}
                            </span>
                        )}
                    </h1>
                    <nav>
                        <a href={releaseUrl(version)}>
                            {l10n.t("Release on GitHub")}
                        </a>
                        <a href={ISSUES_URL}>{l10n.t("Report a problem")}</a>
                        <a href={DISCUSSIONS_URL}>{l10n.t("Discussions")}</a>
                    </nav>
                </header>
                {current === undefined ? (
                    <p className="loading">
                        {l10n.t("No release notes for this version")}
                    </p>
                ) : (
                    <>
                        {current.highlights !== undefined && (
                            <Notes
                                className="highlights"
                                text={current.highlights}
                            />
                        )}
                        {current.migration !== undefined && (
                            <section className="callout">
                                <h2>{l10n.t("After the update")}</h2>
                                <Notes text={current.migration} />
                            </section>
                        )}
                        <Notes text={current.body} />
                    </>
                )}
                {older.length > 0 && (
                    <section className="older">
                        <h2>{l10n.t("Earlier releases")}</h2>
                        {older.map((s) => (
                            <details key={s.version}>
                                <summary>{s.version}</summary>
                                <Notes text={s.markdown} />
                            </details>
                        ))}
                    </section>
                )}
            </div>
        </Shell>
    );
}

createRoot(document.getElementById("root")!).render(<App />);
