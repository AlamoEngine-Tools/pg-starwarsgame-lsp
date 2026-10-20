// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The host side of the item inspector: a tab beside the preview, describing whatever row the
// preview has open.
//
// It holds no scene of its own. The preview owns the three.js side and is the only thing that can
// say what a row IS, so it pushes a subject here; this tab renders it and fetches its own geometry
// pages, which were always a server round trip.
//
// One tab, not one per preview. Two model previews open at once would otherwise each want their
// own inspector, and the reader is looking at one row at a time - so the tab retargets the way the
// preview itself does when it is asked for a different subject.

import * as vscode from "vscode";

import { subMeshGeometryReply } from "./lsp/subMeshGeometry";
import { PanelProject } from "./panelProject";
import {
    PanelRegistry,
    WebviewMessage,
    WebviewPanelHost,
} from "./webviewPanelHost";

/**
 * The page CSS the inspector needs before its bundle runs.
 *
 * The shell scrolls itself rather than the document, so it needs a height to scroll WITHIN -
 * `height: 100%` resolves against #root, and a plain div is auto-height.
 */
const MODEL_INSPECTOR_BODY_STYLE =
    "html, body, #root { height: 100%; margin: 0; padding: 0; }";

export class ModelInspectorPanel extends WebviewPanelHost {
    static readonly viewType = "aetModelInspector";

    /**
     * The open inspectors, at most one per project - filed under the empty subject.
     *
     * Not keyed by what it inspects: it stands for whatever is selected right now, which is not a
     * key. Per project (#276), because the rows it describes come from one project's server.
     */
    private static readonly panels = new PanelRegistry<ModelInspectorPanel>();

    /**
     * The row to describe, held for a webview that has not said `ready` yet.
     *
     * A tab takes a moment to load its bundle, and the press that opens it is also what names the
     * row - so the first subject always arrives before anything is listening. Held here and sent on
     * `ready`, or the tab would open blank and stay that way until the reader clicked a second row.
     */
    private pending: unknown = null;

    /** Whether the webview has come up and been given what it is describing. */
    private live = false;

    private constructor(extensionUri: vscode.Uri, project: PanelProject) {
        super(extensionUri, {
            project,
            viewType: ModelInspectorPanel.viewType,
            title: vscode.l10n.t("Model inspector"),

            // Beside, not Active: the whole reason this is a tab and not a page inside the preview
            // is that the two are read together.
            column: vscode.ViewColumn.Beside,
            script: "modelInspector.js",
            bodyStyle: MODEL_INSPECTOR_BODY_STYLE,

            // A vertex table is exactly the sort of thing a reader hits Ctrl+F in.
            enableFindWidget: true,
        });
    }

    /** Whether the project has a tab open to receive anything - what the preview checks first. */
    static isOpen(projectKey: string): boolean {
        return ModelInspectorPanel.panels.has(projectKey, "");
    }

    /**
     * Opens the inspector on one row, or retargets the open one.
     *
     * @param preserveFocus Keeps the reader in the preview. Opening the inspector is a press on a
     *     row in the tree, and stealing focus to another tab means the next row they click is a
     *     click to come back rather than a click on the row.
     */
    static show(
        extensionUri: vscode.Uri,
        project: PanelProject,
        subject: unknown,
    ): ModelInspectorPanel {
        const panel =
            ModelInspectorPanel.panels.get(project.key, "") ??
            ModelInspectorPanel.panels.track(
                "",
                new ModelInspectorPanel(extensionUri, project),
            );

        panel.setSubject(subject);
        panel.reveal(true);
        return panel;
    }

    /** Retargets the project's inspector if one is open, and does nothing at all if none is. */
    static update(projectKey: string, subject: unknown): void {
        ModelInspectorPanel.panels.get(projectKey, "")?.setSubject(subject);
    }

    static disposeAll(): void {
        ModelInspectorPanel.panels.disposeAll();
    }

    protected async onMessage(message: WebviewMessage): Promise<void> {
        switch (message.type) {
            case "ready":
                this.live = true;
                this.post({ type: "setSubject", subject: this.pending });
                return;

            case "requestSubMeshGeometry":
                this.post(
                    await subMeshGeometryReply(this.project.gateway, message),
                );
                return;

            default:
                return;
        }
    }

    private setSubject(subject: unknown): void {
        if (!this.live) {
            this.pending = subject;
            return;
        }

        this.post({ type: "setSubject", subject });
    }
}
