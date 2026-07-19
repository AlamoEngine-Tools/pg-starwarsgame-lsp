// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as vscode from 'vscode';
import { LanguageClient } from 'vscode-languageclient/node';

interface StoryGraphNodeDto {
    id: string; kind: string; label: string; threadUri?: string | null; line?: number | null;
    eventType?: string | null; rewardType?: string | null; branch?: string | null;
    lifecycle?: string | null; reachable: boolean;
    eventParams?: { position: number; value: string }[] | null;
    rewardParams?: { position: number; value: string }[] | null;
    perpetual?: boolean; storyDialog?: string | null;
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
interface StoryParamSchemaDto {
    position: number; valueType: string; referenceType?: string | null;
    enumName?: string | null; optional: boolean; description?: string | null;
    enumValues?: string[] | null;
}
interface StoryParamOptionDto { value: string; detail?: string | null; }
interface GetStoryParamOptionsResult { options: StoryParamOptionDto[]; error?: string | null; }
interface ResolveStoryReferenceResult {
    uri?: string | null; line: number; column: number; error?: string | null;
}
interface StoryDiagnosticDto {
    nodeId?: string | null; side?: string | null; position?: number | null;
    severity: string; message: string; uri: string; line: number; column: number;
}
interface GetStoryDiagnosticsResult { diagnostics: StoryDiagnosticDto[]; error?: string | null; }
interface StoryTypeSchemaDto {
    name: string; description?: string | null; untested: boolean; params: StoryParamSchemaDto[];
}
interface GetStorySchemaResult {
    eventTypes: StoryTypeSchemaDto[];
    rewardTypes: StoryTypeSchemaDto[];
    error?: string | null;
}
interface ExecuteStoryCommandResult { success: boolean; error?: string | null; }
interface ApplyStoryCommandBatchResult {
    success: boolean; failedIndex?: number | null; error?: string | null;
}
interface WorkspaceSettingsDto {
    skipStoryDeleteConfirmation: boolean; showThreadLanes: boolean; showChapterLanes: boolean;
}
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

    /** Called on `aet/storyGraphChanged` - refreshes only panels whose campaign was invalidated. */
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
    // Mirror of the webview's staged command queue, kept in sync via 'pendingSync'. Lets the panel
    // offer to save if the tab is closed while dirty - the disposed webview can no longer prompt.
    private _pendingCommands: Record<string, unknown>[] = [];
    // Cached "skip the delete-event confirmation" preference (undefined = not fetched yet).
    private _skipDeleteConfirm: boolean | undefined;

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
                localResourceRoots: [
                    vscode.Uri.joinPath(extensionUri, 'out', 'webview'),
                    vscode.Uri.joinPath(extensionUri, 'out', 'codicons'),
                ],
            });
        this._panel.onDidDispose(() => {
            StoryGraphPanel._panels.delete(_campaign);
            void this._promptSaveOnClose();
        });
        const scriptUri = this._panel.webview.asWebviewUri(
            vscode.Uri.joinPath(extensionUri, 'out', 'webview', 'storyGraph.js'));
        const codiconUri = this._panel.webview.asWebviewUri(
            vscode.Uri.joinPath(extensionUri, 'out', 'codicons', 'codicon.css'));
        this._panel.webview.html = buildHtml(scriptUri, codiconUri, this._panel.webview.cspSource);

        this._panel.webview.onDidReceiveMessage(async (msg: { type: string; [key: string]: unknown }) => {
            switch (msg.type) {
                case 'ready':
                    this._sendAvailableModes();
                    await this._sendSchema();
                    await this._sendWorkspaceSettings();
                    await this._sendGraph({});
                    break;
                case 'setLanePref':
                    await this._setLanePrefs(
                        msg.showThreadLanes as boolean, msg.showChapterLanes as boolean);
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
                case 'saveBatch':
                    await this._saveBatch(msg.commands as Record<string, unknown>[]);
                    break;
                case 'validateBatch':
                    await this._validateBatch(msg.commands as Record<string, unknown>[]);
                    break;
                case 'previewGraph':
                    await this._sendPreview(
                        msg.commands as Record<string, unknown>[], msg.filters as GraphFilters);
                    break;
                case 'pendingSync':
                    this._pendingCommands = msg.commands as Record<string, unknown>[];
                    break;
                case 'confirmStage':
                    await this._confirmStage(
                        msg.payload as Record<string, unknown>, msg.confirm as string);
                    break;
                case 'confirmDirtyExit':
                    await this._confirmDirtyExit(msg.next as string);
                    break;
                case 'sim':
                    await this._runSim(msg.method as string, msg.args as Record<string, unknown> | undefined);
                    break;
                case 'paramOptions':
                    await this._sendParamOptions(msg.requestId as number, msg.side as string,
                        msg.typeName as string, msg.position as number, msg.prefix as string | undefined);
                    break;
                case 'resolveRef':
                    await this._resolveRef(msg.value as string, msg.referenceType as string | undefined);
                    break;
            }
        });
    }

    /** Called on `aet/storySimChanged` - the panel's webview re-fetches the sim state. */
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
                // A gesture may have changed the view optimistically (e.g. a picked-off
                // connection) - have the webview re-fetch so it matches reality again.
                this._post({ type: 'invalidate' });
                return;
            }
            if (refreshDetail) { await this._sendDetail(refreshDetail); }
        } catch (e) {
            void vscode.window.showErrorMessage(`EaWEdit: story command failed: ${e}`);
            this._post({ type: 'invalidate' });
        }
    }

    /**
     * Fetches completion candidates for one param slot. Always answers (empty on any failure) —
     * the webview's suggestion dropdown awaits the requestId and must not hang on errors.
     */
    private async _sendParamOptions(
        requestId: number, side: string, typeName: string, position: number, prefix: string | undefined
    ): Promise<void> {
        const reply = (options: StoryParamOptionDto[]): void =>
            this._post({ type: 'paramOptions', requestId, options });
        const client = this._getLspClient();
        if (!client) { reply([]); return; }
        try {
            const result = await client.sendRequest<GetStoryParamOptionsResult>('aet/getStoryParamOptions', {
                campaign: this._campaign, side, typeName, position,
                prefix: prefix || undefined, limit: 50,
            });
            reply(result.error ? [] : result.options ?? []);
        } catch {
            reply([]);
        }
    }

    /** Go-to-definition for a reference-typed param value - opens the XML beside the graph. */
    private async _resolveRef(value: string, referenceType: string | undefined): Promise<void> {
        const client = this._getLspClient();
        if (!client || !value) { return; }
        try {
            const result = await client.sendRequest<ResolveStoryReferenceResult>('aet/resolveStoryReference', {
                value, referenceType,
            });
            if (result.error || !result.uri) {
                void vscode.window.showWarningMessage(`EaWEdit: ${result.error ?? `Cannot resolve '${value}'.`}`);
                return;
            }
            const doc = await vscode.workspace.openTextDocument(vscode.Uri.parse(result.uri));
            const position = new vscode.Position(Math.max(0, result.line), Math.max(0, result.column));
            await vscode.window.showTextDocument(doc, {
                viewColumn: vscode.ViewColumn.Beside,
                selection: new vscode.Range(position, position),
            });
        } catch (e) {
            void vscode.window.showErrorMessage(`EaWEdit: cannot open the definition of '${value}': ${e}`);
        }
    }

    /**
     * Commits a staged edit-mode batch. On success the queue clears client-side and the server's
     * `aet/storyGraphChanged` reconciles the graph; on failure the error (and which change failed)
     * is surfaced and the queue is kept so the user can fix and re-save.
     */
    private async _saveBatch(commands: Record<string, unknown>[]): Promise<void> {
        const client = this._getLspClient();
        if (!client) {
            void vscode.window.showWarningMessage('EaWEdit LSP: server is not running.');
            this._post({ type: 'saveResult', success: false });
            return;
        }
        try {
            const result = await client.sendRequest<ApplyStoryCommandBatchResult>('aet/applyStoryCommandBatch', {
                campaign: this._campaign, commands,
            });
            if (!result.success) {
                const where = typeof result.failedIndex === 'number' ? ` (change ${result.failedIndex + 1})` : '';
                void vscode.window.showErrorMessage(`EaWEdit: ${result.error ?? 'The save failed.'}${where}`);
            }
            this._post({ type: 'saveResult', success: result.success });
        } catch (e) {
            void vscode.window.showErrorMessage(`EaWEdit: save failed: ${e}`);
            this._post({ type: 'saveResult', success: false });
        }
    }

    /**
     * Confirms a destructive staged gesture (delete). Honors the persisted "don't ask again"
     * preference (workspace.settings.json under .aetswg) and lets the modal set it. Replies with
     * `confirmStageResult` so the webview stages the command (or not).
     */
    private async _confirmStage(payload: Record<string, unknown>, confirm: string): Promise<void> {
        if (this._skipDeleteConfirm === undefined) {
            this._skipDeleteConfirm = await this._fetchSkipDeleteConfirm();
        }
        if (this._skipDeleteConfirm) {
            this._post({ type: 'confirmStageResult', proceed: true, payload });
            return;
        }

        const dontAskAgain = "Delete & Don't Ask Again";
        const choice = await vscode.window.showWarningMessage(
            confirm,
            {
                modal: true,
                detail: 'References to this event - prereqs and event-name params (TRIGGER_EVENT, '
                    + 'RESET_EVENT, …) in other events - are NOT removed and will become unresolved. '
                    + 'Run Validate afterwards to find them. Nothing is written until you Save.',
            },
            'Delete', dontAskAgain);

        if (choice === undefined) {
            this._post({ type: 'confirmStageResult', proceed: false, payload });
            return;
        }
        if (choice === dontAskAgain) {
            this._skipDeleteConfirm = true;
            await this._persistSkipDeleteConfirm(true);
        }
        this._post({ type: 'confirmStageResult', proceed: true, payload });
    }

    /**
     * Tells the webview which editor modes it may offer. Edit and Simulation are separately flagged
     * and both default off, so the rotary switch must not advertise a mode whose every request the
     * server would reject - the panel itself is already gated on `tools.storyEditor`, so View is
     * always available by the time this runs. Read here rather than in the webview because only the
     * extension host can see configuration.
     */
    private _sendAvailableModes(): void {
        const features = vscode.workspace.getConfiguration('aet-eaw-edit.features');
        this._post({
            type: 'availableModes',
            edit: features.get<boolean>('tools.storyEditing', false) === true,
            // Not contributed in package.json (WIP): only ever true if hand-written into settings.
            simulate: features.get<boolean>('tools.storySimulator', false) === true,
        });
    }

    /** Fetches the workspace preferences and pushes the swimlane-lane toggles to the webview. */
    private async _sendWorkspaceSettings(): Promise<void> {
        const client = this._getLspClient();
        if (!client) { return; }
        try {
            const settings = await client.sendRequest<WorkspaceSettingsDto>('aet/getWorkspaceSettings', {});
            this._skipDeleteConfirm = settings.skipStoryDeleteConfirmation === true;
            this._post({
                type: 'workspaceSettings',
                showThreadLanes: settings.showThreadLanes === true,
                showChapterLanes: settings.showChapterLanes === true,
            });
        } catch { /* preferences are optional - the graph works without them */ }
    }

    /** Persists the swimlane-lane toggles (best-effort). */
    private async _setLanePrefs(showThreadLanes: boolean, showChapterLanes: boolean): Promise<void> {
        const client = this._getLspClient();
        if (!client) { return; }
        try {
            await client.sendRequest('aet/setWorkspaceSettings', { showThreadLanes, showChapterLanes });
        } catch { /* preference persistence is best-effort */ }
    }

    private async _fetchSkipDeleteConfirm(): Promise<boolean> {
        const client = this._getLspClient();
        if (!client) { return false; }
        try {
            const settings = await client.sendRequest<WorkspaceSettingsDto>('aet/getWorkspaceSettings', {});
            return settings.skipStoryDeleteConfirmation === true;
        } catch {
            return false;
        }
    }

    private async _persistSkipDeleteConfirm(value: boolean): Promise<void> {
        const client = this._getLspClient();
        if (!client) { return; }
        try {
            await client.sendRequest('aet/setWorkspaceSettings', { skipStoryDeleteConfirmation: value });
        } catch { /* preference persistence is best-effort */ }
    }

    /**
     * The tab was closed with unsaved staged changes. A disposed webview can't veto its own close, so
     * this can only offer to flush the mirrored queue after the fact - not cancel the close.
     */
    private async _promptSaveOnClose(): Promise<void> {
        if (this._pendingCommands.length === 0) { return; }
        const choice = await vscode.window.showWarningMessage(
            `The story graph for '${this._campaign}' was closed with ${this._pendingCommands.length} ` +
            'unsaved change(s). Save them?',
            'Save', 'Discard');
        if (choice !== 'Save') { return; }
        const client = this._getLspClient();
        if (!client) {
            void vscode.window.showWarningMessage('EaWEdit LSP: server is not running; changes were not saved.');
            return;
        }
        try {
            const result = await client.sendRequest<ApplyStoryCommandBatchResult>('aet/applyStoryCommandBatch', {
                campaign: this._campaign, commands: this._pendingCommands,
            });
            if (!result.success) {
                const where = typeof result.failedIndex === 'number' ? ` (change ${result.failedIndex + 1})` : '';
                void vscode.window.showErrorMessage(
                    `EaWEdit: could not save the closed story graph${where}: ${result.error ?? ''}`);
            }
        } catch (e) {
            void vscode.window.showErrorMessage(`EaWEdit: could not save the closed story graph: ${e}`);
        }
    }

    /**
     * Prompts before leaving Edit mode with unsaved staged changes and relays the choice back to the
     * webview, which owns the mode state. "Save" saves then switches; "Don't Save" discards and
     * switches; dismissing cancels.
     */
    private async _confirmDirtyExit(next: string): Promise<void> {
        const choice = await vscode.window.showWarningMessage(
            'You have unsaved story changes. Save them before leaving Edit mode?',
            { modal: true }, 'Save', "Don't Save");
        const resolved = choice === 'Save' ? 'save' : choice === "Don't Save" ? 'discard' : 'cancel';
        this._post({ type: 'dirtyExitChoice', choice: resolved, next });
    }

    /**
     * Builds the graph as it would look with the staged batch applied (server-side, no file write)
     * and patches it into the view. This is how structural staged edits - new/deleted/renamed events,
     * new prereq edges - appear before Save without the XML changing on disk.
     */
    private async _sendPreview(commands: Record<string, unknown>[], filters: GraphFilters): Promise<void> {
        const client = this._getLspClient();
        if (!client) { return; }
        try {
            const result = await client.sendRequest<GetStoryGraphResult>('aet/previewStoryGraph', {
                campaign: this._campaign,
                commands,
                nameFilter: filters?.nameFilter || undefined,
                branch: filters?.branch || undefined,
                lifecycle: filters?.lifecycle || undefined,
                reachableFrom: filters?.reachableFrom || undefined,
            });
            if (result.error) {
                void vscode.window.showWarningMessage(`EaWEdit: ${result.error}`);
                return; // keep the current graph rather than blanking it
            }
            let layout: StoryLayoutEntryDto[] = [];
            try {
                const stored = await client.sendRequest<GetStoryLayoutResult>('aet/getStoryLayout', {
                    campaign: this._campaign,
                });
                layout = stored.entries ?? [];
            } catch { /* stored positions are optional - auto-layout covers it */ }
            this._post({
                type: 'graph', preview: true, campaign: this._campaign,
                nodes: result.nodes ?? [], edges: result.edges ?? [], layout,
            });
        } catch (e) {
            void vscode.window.showErrorMessage(`EaWEdit: preview failed: ${e}`);
        }
    }

    /** Dry-runs the staged batch on the server and posts the resulting diagnostics for the pending state. */
    private async _validateBatch(commands: Record<string, unknown>[]): Promise<void> {
        const client = this._getLspClient();
        if (!client) { return; }
        try {
            const result = await client.sendRequest<GetStoryDiagnosticsResult>('aet/validateStoryCommandBatch', {
                campaign: this._campaign, commands,
            });
            if (result.error) { void vscode.window.showWarningMessage(`EaWEdit: ${result.error}`); }
            this._post({ type: 'diagnostics', diagnostics: result.error ? [] : result.diagnostics ?? [] });
        } catch (e) {
            void vscode.window.showErrorMessage(`EaWEdit: validation failed: ${e}`);
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
                eventTypeParams: Object.fromEntries((schema.eventTypes ?? []).map(t => [t.name, t.params ?? []])),
                rewardTypeParams: Object.fromEntries((schema.rewardTypes ?? []).map(t => [t.name, t.params ?? []])),
            });
        } catch { /* schema is styling sugar - the graph renders without it */ }
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
            } catch { /* stored positions are optional - auto-layout covers it */ }
            this._post({
                type: 'graph',
                campaign: this._campaign,
                nodes: result.nodes ?? [],
                edges: result.edges ?? [],
                layout,
            });
            // Diagnostics are NO LONGER pushed on every graph refresh - they were the "live"
            // validation that made editing sluggish. They now come only from the explicit Validate
            // action (aet/validateStoryCommandBatch), which reflects the staged/pending state.
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

function buildHtml(scriptUri: vscode.Uri, codiconUri: vscode.Uri, cspSource: string): string {
    // style-src needs 'unsafe-inline' for styled-components' injected style tags; the codicon
    // stylesheet + its .ttf load from the extension origin (cspSource covers style-src and font-src).
    return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none'; style-src 'unsafe-inline' ${cspSource}; script-src ${cspSource}; font-src ${cspSource}; img-src ${cspSource} data:;">
<link rel="stylesheet" href="${codiconUri}">
</head>
<body>
<div id="root"></div>
<script src="${scriptUri}"></script>
</body>
</html>`;
}
