// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as vscode from 'vscode';
import { LanguageClient } from 'vscode-languageclient/node';

interface StoryGraphNodeDto {
    id: string; kind: string; label: string; threadUri?: string | null; line?: number | null;
    eventType?: string | null; rewardType?: string | null; branch?: string | null;
    lifecycle?: string | null; reachable: boolean;
}
interface StoryGraphEdgeDto { fromId: string; toId: string; kind: string; label?: string | null; }
interface GetStoryGraphResult {
    nodes: StoryGraphNodeDto[]; edges: StoryGraphEdgeDto[]; error?: string | null;
}
interface StoryNodeDetailDto {
    id: string; name: string; threadUri?: string | null; line: number;
    eventType?: string | null; eventFilter?: string | null;
    eventParams: { position: number; value: string }[];
    rewardType?: string | null; rewardParams: { position: number; value: string }[];
    prereqGroups: string[][]; branch?: string | null; perpetual: boolean;
    storyDialog?: string | null; storyChapter?: number | null;
    tags: { name: string; value: string }[];
}
interface GetStoryNodeDetailResult { node?: StoryNodeDetailDto | null; error?: string | null; }
interface GetStorySchemaResult {
    eventTypes: { name: string; untested: boolean }[];
    rewardTypes: { name: string; untested: boolean }[];
    error?: string | null;
}
interface ExecuteStoryCommandResult { success: boolean; error?: string | null; }
interface StoryLayoutEntryDto { file: string; eventName: string; x: number; y: number; }
interface GetStoryLayoutResult { entries: StoryLayoutEntryDto[]; error?: string | null; }

interface GraphFilters {
    nameFilter?: string; branch?: string; lifecycle?: string; reachableFrom?: string;
}

/**
 * Read-only story graph webview, one panel per campaign. The webview (a rete.js app bundled to
 * out/webview/storyGraph.js) is a pure renderer: it holds the current filter state and asks the
 * extension to re-fetch (`fetch` message) whenever filters change or the server pushes
 * `aet/storyGraphChanged` for this campaign (`invalidate` → the webview replays its filters so
 * the refreshed graph keeps the user's view).
 */
export class StoryGraphPanel {
    private static readonly _panels = new Map<string, StoryGraphPanel>();

    static show(
        campaign: string,
        extensionUri: vscode.Uri,
        getLspClient: () => LanguageClient | undefined
    ): void {
        const existing = StoryGraphPanel._panels.get(campaign);
        if (existing) {
            existing._panel.reveal();
            return;
        }
        StoryGraphPanel._panels.set(campaign, new StoryGraphPanel(campaign, extensionUri, getLspClient));
    }

    /** Called on `aet/storyGraphChanged` — refreshes only panels whose campaign was invalidated. */
    static refreshInvalidated(campaigns: string[]): void {
        for (const campaign of campaigns) {
            StoryGraphPanel._panels.get(campaign)?._post({ type: 'invalidate' });
        }
    }

    static disposeAll(): void {
        for (const panel of [...StoryGraphPanel._panels.values()]) {
            panel._panel.dispose();
        }
    }

    private readonly _panel: vscode.WebviewPanel;

    private constructor(
        private readonly _campaign: string,
        extensionUri: vscode.Uri,
        private readonly _getLspClient: () => LanguageClient | undefined
    ) {
        this._panel = vscode.window.createWebviewPanel(
            'aetStoryGraph', `Story: ${_campaign}`, vscode.ViewColumn.Active,
            {
                enableScripts: true,
                retainContextWhenHidden: true,
                localResourceRoots: [vscode.Uri.joinPath(extensionUri, 'out', 'webview')],
            });
        this._panel.onDidDispose(() => StoryGraphPanel._panels.delete(_campaign));
        const scriptUri = this._panel.webview.asWebviewUri(
            vscode.Uri.joinPath(extensionUri, 'out', 'webview', 'storyGraph.js'));
        this._panel.webview.html = buildHtml(scriptUri, this._panel.webview.cspSource);

        this._panel.webview.onDidReceiveMessage(async (msg: { type: string; [key: string]: unknown }) => {
            switch (msg.type) {
                case 'ready':
                    await this._sendSchema();
                    await this._sendGraph({});
                    break;
                case 'fetch':
                    await this._sendGraph(msg.filters as GraphFilters);
                    break;
                case 'detail':
                    await this._sendDetail(msg.nodeId as string);
                    break;
                case 'openXml':
                    await this._openXml(msg.threadUri as string, msg.line as number | undefined);
                    break;
                case 'command':
                    await this._runCommand(
                        msg.payload as Record<string, unknown>,
                        msg.confirm as string | undefined,
                        msg.refreshDetail as string | undefined);
                    break;
                case 'saveLayout':
                    await this._saveLayout(msg.entries as StoryLayoutEntryDto[]);
                    break;
                case 'sim':
                    await this._runSim(msg.method as string, msg.args as Record<string, unknown> | undefined);
                    break;
            }
        });
    }

    /** Called on `aet/storySimChanged` — the panel's webview re-fetches the sim state. */
    static simChanged(campaign: string): void {
        StoryGraphPanel._panels.get(campaign)?._post({ type: 'simChanged' });
    }

    /**
     * Forwards a simulation request (`start`, `stop`, `getState`, `satisfyTrigger`, `setFlag`,
     * `advanceClock`, `luaNotify`) and posts the resulting state document back.
     */
    private async _runSim(method: string, args: Record<string, unknown> | undefined): Promise<void> {
        const client = this._getLspClient();
        if (!client) {
            void vscode.window.showWarningMessage('EaWEdit LSP: server is not running.');
            return;
        }
        const requestName = 'aet/storySim' + method.charAt(0).toUpperCase() + method.slice(1);
        try {
            const result = await client.sendRequest<{ state?: unknown; error?: string | null }>(
                requestName, { campaign: this._campaign, ...(args ?? {}) });
            if (result.error) {
                void vscode.window.showErrorMessage(`EaWEdit: ${result.error}`);
                return;
            }
            this._post({ type: 'simState', state: result.state ?? null });
        } catch (e) {
            void vscode.window.showErrorMessage(`EaWEdit: simulation request failed: ${e}`);
        }
    }

    /**
     * Executes a mutation. `confirm` shows a modal first (destructive ops); `refreshDetail`
     * re-fetches the given node's property view after success so the panel doesn't go stale
     * while the graph re-render is still in flight.
     */
    private async _runCommand(
        payload: Record<string, unknown>, confirm: string | undefined, refreshDetail: string | undefined
    ): Promise<void> {
        const client = this._getLspClient();
        if (!client) {
            void vscode.window.showWarningMessage('EaWEdit LSP: server is not running.');
            return;
        }
        if (confirm) {
            const choice = await vscode.window.showWarningMessage(confirm, { modal: true }, 'Continue');
            if (choice !== 'Continue') { return; }
        }
        try {
            const result = await client.sendRequest<ExecuteStoryCommandResult>('aet/executeStoryCommand', {
                campaign: this._campaign,
                ...payload,
            });
            if (!result.success) {
                void vscode.window.showErrorMessage(`EaWEdit: ${result.error ?? 'The story command failed.'}`);
                return;
            }
            if (refreshDetail) { await this._sendDetail(refreshDetail); }
        } catch (e) {
            void vscode.window.showErrorMessage(`EaWEdit: story command failed: ${e}`);
        }
    }

    private async _saveLayout(entries: StoryLayoutEntryDto[]): Promise<void> {
        const client = this._getLspClient();
        if (!client || !entries?.length) { return; }
        try {
            await client.sendRequest('aet/setStoryLayout', { campaign: this._campaign, entries });
        } catch { /* layout persistence is best-effort */ }
    }

    private async _sendSchema(): Promise<void> {
        const client = this._getLspClient();
        if (!client) { return; }
        try {
            const schema = await client.sendRequest<GetStorySchemaResult>('aet/getStorySchema', {});
            this._post({
                type: 'schema',
                eventTypes: (schema.eventTypes ?? []).map(t => t.name),
                rewardTypes: (schema.rewardTypes ?? []).map(t => t.name),
                untestedEventTypes: (schema.eventTypes ?? []).filter(t => t.untested).map(t => t.name),
                untestedRewardTypes: (schema.rewardTypes ?? []).filter(t => t.untested).map(t => t.name),
            });
        } catch { /* schema is styling sugar — the graph renders without it */ }
    }

    private async _sendGraph(filters: GraphFilters): Promise<void> {
        const client = this._getLspClient();
        if (!client) {
            this._post({ type: 'error', message: 'LSP server is not running.' });
            return;
        }
        try {
            const result = await client.sendRequest<GetStoryGraphResult>('aet/getStoryGraph', {
                campaign: this._campaign,
                nameFilter: filters.nameFilter || undefined,
                branch: filters.branch || undefined,
                lifecycle: filters.lifecycle || undefined,
                reachableFrom: filters.reachableFrom || undefined,
            });
            if (result.error) {
                this._post({ type: 'error', message: result.error });
                return;
            }
            let layout: StoryLayoutEntryDto[] = [];
            try {
                const stored = await client.sendRequest<GetStoryLayoutResult>('aet/getStoryLayout', {
                    campaign: this._campaign,
                });
                layout = stored.entries ?? [];
            } catch { /* stored positions are optional — auto-layout covers it */ }
            this._post({
                type: 'graph',
                campaign: this._campaign,
                nodes: result.nodes ?? [],
                edges: result.edges ?? [],
                layout,
            });
        } catch (e) {
            this._post({ type: 'error', message: `Cannot load story graph: ${e}` });
        }
    }

    private async _sendDetail(nodeId: string): Promise<void> {
        const client = this._getLspClient();
        if (!client) { return; }
        try {
            const result = await client.sendRequest<GetStoryNodeDetailResult>('aet/getStoryNodeDetail', {
                campaign: this._campaign, nodeId,
            });
            this._post({ type: 'detail', node: result.node ?? null, error: result.error ?? null });
        } catch (e) {
            this._post({ type: 'detail', node: null, error: String(e) });
        }
    }

    private async _openXml(threadUri: string, line: number | undefined): Promise<void> {
        if (!threadUri) { return; }
        try {
            const doc = await vscode.workspace.openTextDocument(vscode.Uri.parse(threadUri));
            const position = new vscode.Position(Math.max(0, line ?? 0), 0);
            await vscode.window.showTextDocument(doc, {
                viewColumn: vscode.ViewColumn.Beside,
                selection: new vscode.Range(position, position),
            });
        } catch (e) {
            void vscode.window.showErrorMessage(`EaWEdit: cannot open ${threadUri}: ${e}`);
        }
    }

    private _post(msg: unknown): void {
        void this._panel.webview.postMessage(msg);
    }
}

function buildHtml(scriptUri: vscode.Uri, cspSource: string): string {
    // style-src needs 'unsafe-inline' for styled-components' injected style tags.
    return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none'; style-src 'unsafe-inline' ${cspSource}; script-src ${cspSource}; font-src ${cspSource}; img-src ${cspSource} data:;">
</head>
<body>
<div id="root"></div>
<script src="${scriptUri}"></script>
</body>
</html>`;
}
