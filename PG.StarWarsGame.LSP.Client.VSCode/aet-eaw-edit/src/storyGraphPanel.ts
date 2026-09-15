// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as vscode from 'vscode';

import { LspGateway } from './lsp/lspGateway';
import { revealDefinition } from './revealDefinition';
import { panelKey, panelTitle, type StoryGraphTarget } from './storyGraphTarget';
import { PanelRegistry, WebviewMessage, WebviewPanelHost } from './webviewPanelHost';
import {
    ApplyStoryCommandBatchResult, ExecuteStoryCommandResult, GetStoryDiagnosticsResult,
    GetStoryGraphResult, GetStoryLayoutResult, GetStoryNodeDetailResult,
    GetStoryParamOptionsResult, GetStorySchemaResult, GraphFilters,
    StoryLayoutEntryDto, StoryParamOptionDto, StorySimStateResult, WorkspaceSettingsDto,
} from './protocol';

/**
 * Read-only story graph webview, one panel per campaign FACTION - a campaign's factions are
 * separate story chains and never share a graph. The webview (a rete.js app bundled to
 * out/webview/storyGraph.js) is a pure renderer: it holds the current filter state and asks the
 * extension to re-fetch (`fetch` message) whenever filters change or the server pushes
 * `aet/storyGraphChanged` for this campaign (`invalidate` → the webview replays its filters so
 * the refreshed graph keeps the user's view).
 */
export class StoryGraphPanel extends WebviewPanelHost {
    private static readonly _panels = new PanelRegistry<StoryGraphPanel>();

    static show(target: StoryGraphTarget, extensionUri: vscode.Uri, lsp: LspGateway): void {
        const key = panelKey(target);
        const existing = StoryGraphPanel._panels.get(key);
        if (existing) {
            existing.reveal();
            return;
        }
        StoryGraphPanel._panels.track(key, new StoryGraphPanel(target, extensionUri, lsp));
    }

    /**
     * Called on `aet/storyGraphChanged` - refreshes the panels the change reached.
     *
     * The notification names CAMPAIGNS while a panel is one campaign faction, so every faction of
     * an invalidated campaign refreshes. That is wider than it needs to be and deliberately so: a
     * thread can be listed by more than one faction's manifest, so narrowing by faction here would
     * have to repeat the server's closure walk to stay correct.
     */
    static refreshInvalidated(campaigns: string[]): void {
        const invalidated = new Set(campaigns.map(c => c.toLowerCase()));
        for (const panel of StoryGraphPanel._panels.all) {
            if (invalidated.has(panel._target.campaign.toLowerCase())) {
                panel.post({ type: 'invalidate' });
            }
        }
    }

    static disposeAll(): void {
        StoryGraphPanel._panels.disposeAll();
    }

    // Mirror of the webview's staged command queue, kept in sync via 'pendingSync'. Lets the panel
    // offer to save if the tab is closed while dirty - the disposed webview can no longer prompt.
    private _pendingCommands: Record<string, unknown>[] = [];
    // Cached "skip the delete-event confirmation" preference (undefined = not fetched yet).
    private _skipDeleteConfirm: boolean | undefined;

    private constructor(
        private readonly _target: StoryGraphTarget,
        extensionUri: vscode.Uri,
        private readonly _lsp: LspGateway
    ) {
        // No bodyStyle: this webview resets the page from inside its own styled-components global,
        // which is where the rest of its canvas styling lives.
        super(extensionUri, {
            viewType: 'aetStoryGraph',
            title: panelTitle(_target),
            column: vscode.ViewColumn.Active,
            script: 'storyGraph.js',
        });

        this.onDidDispose(() => void this._promptSaveOnClose());
    }

    protected async onMessage(msg: WebviewMessage): Promise<void> {
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
    }

    /** Called on `aet/storySimChanged` - the panel's webview re-fetches the sim state. */
    static simChanged(target: StoryGraphTarget): void {
        StoryGraphPanel._panels.get(panelKey(target))?.post({ type: 'simChanged' });
    }

    /**
     * Forwards a simulation request (`start`, `stop`, `getState`, `satisfyTrigger`, `setFlag`,
     * `advanceClock`, `luaNotify`) and posts the resulting state document back.
     */
    private async _runSim(method: string, args: Record<string, unknown> | undefined): Promise<void> {
        const requestName = 'aet/storySim' + method.charAt(0).toUpperCase() + method.slice(1);
        const result = await this._lsp.requestOrReport<StorySimStateResult>(
            requestName, { campaign: this._target.campaign, faction: this._target.faction, ...(args ?? {}) }, 'simulation request failed');
        if (result === undefined) { return; }

        if (result.error) {
            void vscode.window.showErrorMessage(`EaWEdit: ${result.error}`);
            return;
        }
        this.post({ type: 'simState', state: result.state ?? null });
    }

    /**
     * Executes a mutation. `confirm` shows a modal first (destructive ops); `refreshDetail`
     * re-fetches the given node's property view after success so the panel doesn't go stale
     * while the graph re-render is still in flight.
     */
    private async _runCommand(
        payload: Record<string, unknown>, confirm: string | undefined, refreshDetail: string | undefined
    ): Promise<void> {
        if (!this._lsp.requireRunning()) { return; }

        if (confirm) {
            const choice = await vscode.window.showWarningMessage(confirm, { modal: true }, 'Continue');
            if (choice !== 'Continue') { return; }
        }

        const result = await this._lsp.requestOrReport<ExecuteStoryCommandResult>(
            'aet/executeStoryCommand', { campaign: this._target.campaign, faction: this._target.faction, ...payload },
            'story command failed');

        // Either a failed request or a refused command: a gesture may have changed the view
        // optimistically (e.g. a picked-off connection), so have the webview re-fetch and match
        // reality again.
        if (result === undefined) { this.post({ type: 'invalidate' }); return; }

        if (!result.success) {
            void vscode.window.showErrorMessage(
                `EaWEdit: ${result.error ?? 'The story command failed.'}`);
            this.post({ type: 'invalidate' });
            return;
        }

        if (refreshDetail) { await this._sendDetail(refreshDetail); }
    }

    /**
     * Fetches completion candidates for one param slot. Always answers (empty on any failure) —
     * the webview's suggestion dropdown awaits the requestId and must not hang on errors.
     */
    private async _sendParamOptions(
        requestId: number, side: string, typeName: string, position: number, prefix: string | undefined
    ): Promise<void> {
        // Always answers, empty on any failure - the webview's suggestion dropdown awaits this
        // requestId and would hang forever on a silent return.
        const result = await this._lsp.requestOr<GetStoryParamOptionsResult>(
            'aet/getStoryParamOptions',
            {
                campaign: this._target.campaign, faction: this._target.faction, side, typeName, position,
                prefix: prefix || undefined, limit: 50,
            },
            { options: [] });

        this.post({
            type: 'paramOptions', requestId,
            options: (result.error ? [] : result.options ?? []) satisfies StoryParamOptionDto[],
        });
    }

    /** Go-to-definition for a reference-typed param value - opens the XML beside the graph. */
    private async _resolveRef(value: string, referenceType: string | undefined): Promise<void> {
        // Its own endpoint rather than the shared one: this lookup is the story editor's, and the
        // server keeps it behind that feature's flag. The caller-side handling is identical, so
        // only the method name differs.
        await revealDefinition(this._lsp, value, referenceType, 'aet/resolveStoryReference');
    }

    /**
     * Commits a staged edit-mode batch. On success the queue clears client-side and the server's
     * `aet/storyGraphChanged` reconciles the graph; on failure the error (and which change failed)
     * is surfaced and the queue is kept so the user can fix and re-save.
     */
    private async _saveBatch(commands: Record<string, unknown>[]): Promise<void> {
        const result = await this._lsp.requestOrReport<ApplyStoryCommandBatchResult>(
            'aet/applyStoryCommandBatch', { campaign: this._target.campaign, faction: this._target.faction, commands }, 'save failed');

        // The webview needs an answer either way: without one the Save button stays spinning and
        // the queue is neither cleared nor released for another attempt.
        if (result === undefined) { this.post({ type: 'saveResult', success: false }); return; }

        if (!result.success) {
            const where = typeof result.failedIndex === 'number'
                ? ` (change ${result.failedIndex + 1})` : '';
            void vscode.window.showErrorMessage(
                `EaWEdit: ${result.error ?? 'The save failed.'}${where}`);
        }
        this.post({ type: 'saveResult', success: result.success });
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
            this.post({ type: 'confirmStageResult', proceed: true, payload });
            return;
        }

        const dontAskAgain = "Delete & Don't Ask Again";
        const choice = await vscode.window.showWarningMessage(
            confirm,
            {
                modal: true,
                detail: 'References to this event - prereqs and event-name params (TRIGGER_EVENT, '
                    + 'RESET_EVENT, and so on) in other events - are NOT removed and will become unresolved. '
                    + 'Run Validate afterwards to find them. Nothing is written until you Save.',
            },
            'Delete', dontAskAgain);

        if (choice === undefined) {
            this.post({ type: 'confirmStageResult', proceed: false, payload });
            return;
        }
        if (choice === dontAskAgain) {
            this._skipDeleteConfirm = true;
            await this._persistSkipDeleteConfirm(true);
        }
        this.post({ type: 'confirmStageResult', proceed: true, payload });
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
        this.post({
            type: 'availableModes',
            edit: features.get<boolean>('tools.storyEditing', false) === true,
            // Not contributed in package.json (WIP): only ever true if hand-written into settings.
            simulate: features.get<boolean>('tools.storySimulator', false) === true,
        });
    }

    /** Fetches the workspace preferences and pushes the swimlane-lane toggles to the webview. */
    private async _sendWorkspaceSettings(): Promise<void> {
        // Preferences are optional - the graph works without them, so a failure is not reported.
        const settings = await this._lsp.request<WorkspaceSettingsDto>('aet/getWorkspaceSettings');
        if (!settings.ok) { return; }

        this._skipDeleteConfirm = settings.value.skipStoryDeleteConfirmation === true;
        this.post({
            type: 'workspaceSettings',
            showThreadLanes: settings.value.showThreadLanes === true,
            showChapterLanes: settings.value.showChapterLanes === true,
        });
    }

    /** Persists the swimlane-lane toggles (best-effort). */
    private async _setLanePrefs(showThreadLanes: boolean, showChapterLanes: boolean): Promise<void> {
        await this._lsp.notify('aet/setWorkspaceSettings', { showThreadLanes, showChapterLanes });
    }

    private async _fetchSkipDeleteConfirm(): Promise<boolean> {
        // Defaults to asking: a preference that could not be read must not silently skip a
        // confirmation the user never turned off.
        const settings = await this._lsp.requestOr<WorkspaceSettingsDto>(
            'aet/getWorkspaceSettings', {},
            { skipStoryDeleteConfirmation: false, showThreadLanes: false, showChapterLanes: false });
        return settings.skipStoryDeleteConfirmation === true;
    }

    private async _persistSkipDeleteConfirm(value: boolean): Promise<void> {
        await this._lsp.notify('aet/setWorkspaceSettings', { skipStoryDeleteConfirmation: value });
    }

    /**
     * The tab was closed with unsaved staged changes. A disposed webview can't veto its own close, so
     * this can only offer to flush the mirrored queue after the fact - not cancel the close.
     */
    private async _promptSaveOnClose(): Promise<void> {
        if (this._pendingCommands.length === 0) { return; }
        const choice = await vscode.window.showWarningMessage(
            `The story graph for '${this._target.campaign}' was closed with ${this._pendingCommands.length} ` +
            'unsaved change(s). Save them?',
            'Save', 'Discard');
        if (choice !== 'Save') { return; }

        if (!this._lsp.isRunning) {
            void vscode.window.showWarningMessage(
                'EaWEdit LSP: Server is not running; changes were not saved.');
            return;
        }

        const result = await this._lsp.requestOrReport<ApplyStoryCommandBatchResult>(
            'aet/applyStoryCommandBatch',
            { campaign: this._target.campaign, faction: this._target.faction, commands: this._pendingCommands },
            'could not save the closed story graph');
        if (result === undefined || result.success) { return; }

        const where = typeof result.failedIndex === 'number' ? ` (change ${result.failedIndex + 1})` : '';
        void vscode.window.showErrorMessage(
            `EaWEdit: Could not save the closed story graph${where} - ${result.error ?? ''}`);
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
        this.post({ type: 'dirtyExitChoice', choice: resolved, next });
    }

    /**
     * Builds the graph as it would look with the staged batch applied (server-side, no file write)
     * and patches it into the view. This is how structural staged edits - new/deleted/renamed events,
     * new prereq edges - appear before Save without the XML changing on disk.
     */
    private async _sendPreview(commands: Record<string, unknown>[], filters: GraphFilters): Promise<void> {
        const result = await this._lsp.requestOrReport<GetStoryGraphResult>(
            'aet/previewStoryGraph',
            { campaign: this._target.campaign, faction: this._target.faction, commands, ...filterFields(filters) }, 'preview failed');
        if (result === undefined) { return; }

        if (result.error) {
            void vscode.window.showWarningMessage(`EaWEdit: ${result.error}`);
            return; // keep the current graph rather than blanking it
        }

        this.post({
            type: 'graph', preview: true, campaign: this._target.campaign, faction: this._target.faction,
            nodes: result.nodes ?? [], edges: result.edges ?? [],
            branches: result.branches ?? undefined, threads: result.threads ?? undefined,
            layout: await this._layout(),
        });
    }

    /** Dry-runs the staged batch on the server and posts the resulting diagnostics for the pending state. */
    private async _validateBatch(commands: Record<string, unknown>[]): Promise<void> {
        const result = await this._lsp.requestOrReport<GetStoryDiagnosticsResult>(
            'aet/validateStoryCommandBatch', { campaign: this._target.campaign, faction: this._target.faction, commands },
            'validation failed');
        if (result === undefined) { return; }

        if (result.error) { void vscode.window.showWarningMessage(`EaWEdit: ${result.error}`); }
        this.post({
            type: 'diagnostics', diagnostics: result.error ? [] : result.diagnostics ?? [],
        });
    }

    private async _saveLayout(entries: StoryLayoutEntryDto[]): Promise<void> {
        if (!entries?.length) { return; }
        await this._lsp.notify('aet/setStoryLayout', { campaign: this._target.campaign, faction: this._target.faction, entries });
    }

    private async _sendSchema(): Promise<void> {
        // Schema is styling sugar - the graph renders without it, so a failure stays quiet.
        const schema = await this._lsp.request<GetStorySchemaResult>('aet/getStorySchema');
        if (!schema.ok) { return; }

        const events = schema.value.eventTypes ?? [];
        const rewards = schema.value.rewardTypes ?? [];
        this.post({
            type: 'schema',
            eventTypes: events.map(t => t.name),
            rewardTypes: rewards.map(t => t.name),
            untestedEventTypes: events.filter(t => t.untested).map(t => t.name),
            untestedRewardTypes: rewards.filter(t => t.untested).map(t => t.name),
            eventTypeParams: Object.fromEntries(events.map(t => [t.name, t.params ?? []])),
            rewardTypeParams: Object.fromEntries(rewards.map(t => [t.name, t.params ?? []])),
        });
    }

    /**
     * The stored node positions, or none.
     *
     * Optional data: with no sidecar the webview auto-lays the graph out, so a failure here is not
     * worth a word. Shared by the live graph and the staged preview, which must place the same node
     * in the same spot or the preview looks like a different graph.
     */
    private async _layout(): Promise<StoryLayoutEntryDto[]> {
        const stored = await this._lsp.requestOr<GetStoryLayoutResult>(
            'aet/getStoryLayout', { campaign: this._target.campaign, faction: this._target.faction }, { entries: [] });
        return stored.entries ?? [];
    }

    private async _sendGraph(filters: GraphFilters): Promise<void> {
        // Reported into the webview rather than as a notification: this is the panel's whole
        // content, so the message belongs where the graph would have been.
        const outcome = await this._lsp.request<GetStoryGraphResult>(
            'aet/getStoryGraph', { campaign: this._target.campaign, faction: this._target.faction, ...filterFields(filters) });

        if (!outcome.ok) {
            this.post({
                type: 'error',
                message: outcome.reason === 'offline'
                    ? 'LSP server is not running.'
                    : `Cannot load story graph: ${outcome.message}`,
            });
            return;
        }

        if (outcome.value.error) {
            this.post({ type: 'error', message: outcome.value.error });
            return;
        }

        this.post({
            type: 'graph',
            campaign: this._target.campaign, faction: this._target.faction,
            nodes: outcome.value.nodes ?? [],
            edges: outcome.value.edges ?? [],
            branches: outcome.value.branches ?? undefined,
            threads: outcome.value.threads ?? undefined,
            layout: await this._layout(),
        });
        // Diagnostics are NO LONGER pushed on every graph refresh - they were the "live"
        // validation that made editing sluggish. They now come only from the explicit Validate
        // action (aet/validateStoryCommandBatch), which reflects the staged/pending state.
    }

    private async _sendDetail(nodeId: string): Promise<void> {
        const outcome = await this._lsp.request<GetStoryNodeDetailResult>(
            'aet/getStoryNodeDetail', { campaign: this._target.campaign, faction: this._target.faction, nodeId });

        // The property view shows the failure in place; a notification would be a modal over a
        // panel that is already able to say what is wrong.
        this.post(outcome.ok
            ? { type: 'detail', node: outcome.value.node ?? null, error: outcome.value.error ?? null }
            : { type: 'detail', node: null, error: outcome.message });
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
            void vscode.window.showErrorMessage(`EaWEdit: Cannot open ${threadUri} - ${e}`);
        }
    }

}

/**
 * Spreads the filter state into the four request fields the server takes.
 *
 * An empty filter is sent as absent rather than as an empty string - the server reads a present
 * field as "filter by this", so `branch: ''` would ask for the events whose branch is literally
 * empty. Written once because the live graph and the staged preview must filter identically, or
 * previewing an edit silently changes which nodes are on screen.
 */
function filterFields(filters: GraphFilters | undefined): Record<string, string | undefined> {
    return {
        nameFilter: filters?.nameFilter || undefined,
        branch: filters?.branch || undefined,
        lifecycle: filters?.lifecycle || undefined,
        reachableFrom: filters?.reachableFrom || undefined,
        plotState: filters?.plotState || undefined,
    };
}

