// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The host side of the 3D preview: it owns the panel, answers the webview's asset requests, and
// launches the standalone editors.
//
// Assets are fetched one at a time on request rather than pushed with the scene. A capital ship is
// several megabytes of geometry across a dozen models plus its textures, and shipping that as one
// message would stall the extension host and leave the panel blank until all of it arrived.

import * as vscode from 'vscode';

import { LspGateway } from './lsp/lspGateway';
import { subMeshGeometryReply } from './lsp/subMeshGeometry';
import { ModelInspectorPanel } from './modelInspectorPanel';
import {
    GetModelDetailResult, GetModelGlbResult, GetModelTextureResult, GetParticleSystemResult,
    GetProjectileResult,
    GetPreviewSceneResult,
    GetShaderSourceResult,
} from './protocol/modelPreview';
import { revealDefinition } from './revealDefinition';
import { subjectStateFrom, type SubjectState } from './webview/preview/subjectState';
import { readViewerSettings, saveViewerSettings } from './viewerSettingsStorage';
import { readProjectSettings, saveProjectSettings } from './projectSettingsStorage';
import { PanelRegistry, WebviewPanelHost, WebviewMessage, panelKey } from './webviewPanelHost';

/** What the preview is showing, and therefore what to ask the server for. */
export type PreviewSubject =
    | { kind: 'object'; objectId: string }
    | { kind: 'model'; modelReference: string }
    | { kind: 'animation'; animationReference: string };

/** External tools the preview can hand off to. */
interface ExternalTool {
    setting: string;
    label: string;
}

const ALO_VIEWER: ExternalTool = {
    setting: 'aet-eaw-edit.tools.aloViewerExecutable',
    label: 'AloViewer',
};

const PARTICLE_EDITOR: ExternalTool = {
    setting: 'aet-eaw-edit.tools.particleEditorExecutable',
    label: 'Particle Editor',
};

/**
 * The page CSS the preview needs before its bundle runs.
 *
 * Exported so the playwright harness can serve the same rules rather than its own approximation.
 * The harness had `#root { height: 100% }` and the panel did not, so the viewport filled the frame
 * in every screenshot and collapsed to a sliver in VS Code - the one difference that mattered was
 * the one the harness invented.
 */
export const MODEL_PREVIEW_BODY_STYLE =
    'html, body, #root { height: 100%; margin: 0; padding: 0; overflow: hidden; }';

/**
 * The energy toggle, off by default.
 *
 * The mechanic is implemented in the engine and works, but the shipped game disables it and offers
 * no interface for it - so a modder has to turn it on deliberately, having read what the setting
 * says, rather than finding a pool in the preview that no player will ever see.
 */
const ENERGY_SETTING = 'aet-eaw-edit.features.preview.energyPool';

export class ModelPreviewPanel extends WebviewPanelHost {
    static readonly viewType = 'aetModelPreview';
    private static readonly panels = new PanelRegistry<ModelPreviewPanel>();

    private subject: PreviewSubject;

    /**
     * Textures already asked for.
     *
     * Every part that loads reports the whole scene's texture list, so without this a ten-part unit
     * would request its shared hull texture ten times.
     */
    private readonly requestedTextures = new Set<string>();

    /**
     * Every open preview, for the refresh push.
     *
     * Separate from `panels`, which keys the previews this class OPENED. A preview adopted for a
     * custom editor tab is deliberately absent from that registry - VS Code owns its lifetime and
     * tracking it would risk a double dispose - but a `.alo` opened as a file is exactly as stale
     * as any other when the tree behind it changes, so refresh has to reach it. This set holds
     * both and disposes of nothing.
     */
    private static readonly live = new Set<ModelPreviewPanel>();

    /**
     * The scene as last sent, serialised.
     *
     * Compared on a refresh so an edit somewhere else in the workspace costs one request and
     * nothing more. Without it every save would re-send the scene, and the webview answers a scene
     * by asking for every part's geometry again.
     */
    private lastScene: string | null = null;

    private constructor(
        // Kept, not just passed up: opening the inspector tab needs it again later.
        private readonly extensionUri: vscode.Uri,
        private readonly lsp: LspGateway,
        subject: PreviewSubject,
        title: string,
        adopt?: vscode.WebviewPanel,
    ) {
        super(extensionUri, {
            viewType: ModelPreviewPanel.viewType,
            title,
            column: vscode.ViewColumn.Active,
            script: 'modelPreview.js',
            // #root needs the height too, not just html and body. The shell is `height: 100%`, which
            // resolves against #root - and a plain div is auto-height, so without this the whole
            // preview collapses to its content and sits squashed against the top of the tab with the
            // viewport a few pixels tall.
            bodyStyle: MODEL_PREVIEW_BODY_STYLE,
            // three.js hands decoded texture pixels to the GPU through a blob URL.
            allowBlobImages: true,
        }, adopt);

        this.subject = subject;

        ModelPreviewPanel.live.add(this);
        this.onDidDispose(() => ModelPreviewPanel.live.delete(this));

        // The energy toggle, pushed again whenever it changes. Only the extension host can read
        // configuration, so the webview cannot ask - and a reader who turns energy on wants to see
        // it in the preview they already have open, not after a reload.
        const watch = vscode.workspace.onDidChangeConfiguration(event => {
            if (event.affectsConfiguration(ENERGY_SETTING)) {
                this.sendPreviewFeatures();
            }
        });

        this.onDidDispose(() => watch.dispose());
    }

    /**
     * Which optional mechanics this preview shows.
     *
     * The webview defaults every one of them to OFF, so a panel that never receives this - an older
     * host, a message lost on startup - stays in the safe state rather than offering a mechanic the
     * shipped game disables.
     */
    private sendPreviewFeatures(): void {
        this.post({
            type: 'previewFeatures',
            features: {
                energyPool: vscode.workspace.getConfiguration()
                    .get<boolean>(ENERGY_SETTING) === true,
            },
        });
    }

    /**
     * Drives a panel VS Code created for a custom editor.
     *
     * Not tracked in the registry: VS Code owns an editor tab's lifetime and already prevents a
     * second tab for the same file, so tracking it would only risk disposing a panel twice.
     */
    static adopt(
        extensionUri: vscode.Uri, lsp: LspGateway, subject: PreviewSubject, title: string,
        panel: vscode.WebviewPanel,
    ): ModelPreviewPanel {
        return new ModelPreviewPanel(extensionUri, lsp, subject, title, panel);
    }

    /** Opens, or reveals and retargets, the preview for one subject. */
    static show(
        extensionUri: vscode.Uri, lsp: LspGateway, subject: PreviewSubject, title: string,
    ): ModelPreviewPanel {
        // One panel per subject: opening the same model twice should not give two GPU contexts, and
        // two panels for one thing is a way to run out of them.
        const key = panelKey(subjectKey(subject));
        const existing = ModelPreviewPanel.panels.get(key);

        if (existing !== undefined) {
            existing.reveal();
            return existing;
        }

        const panel = new ModelPreviewPanel(extensionUri, lsp, subject, title);
        return ModelPreviewPanel.panels.track(key, panel);
    }

    /**
     * Per-subject state, for as long as the window lives.
     *
     * Keyed by the same key the panel tracker uses, and held on the CLASS rather than the instance
     * so closing a preview and reopening the same subject picks up where it left off. Deliberately
     * not written to disk - see `setSubjectState`.
     */
    private static readonly subjectStates = new Map<string, SubjectState>();

    /** The clips this subject's model has, as the scene reported them. */
    private sceneAnimations: string[] = [];

    private get stateKey(): string {
        return subjectKey(this.subject);
    }

    static disposeAll(): void {
        ModelPreviewPanel.panels.disposeAll();
    }

    /**
     * Writes a rendered frame to a file the reader chooses.
     *
     * The webview hands over bytes because it CANNOT do this itself: a webview has no filesystem,
     * and a `data:` link in one is inert - the viewer's sandbox blocks a download the page starts.
     * So the picture is made where the GPU is and written where the API is.
     */
    private async saveCapture(message: WebviewMessage): Promise<void> {
        const dataUrl = String(message.dataUrl ?? '');
        const comma = dataUrl.indexOf(',');

        if (!dataUrl.startsWith('data:image/png;base64,') || comma < 0) {
            void vscode.window.showErrorMessage('The preview produced no image to save.');
            return;
        }

        const target = await vscode.window.showSaveDialog({
            defaultUri: vscode.Uri.file(String(message.fileName ?? 'capture.png')),
            filters: { Images: ['png'] },
        });

        if (target === undefined) {
            return;
        }

        await vscode.workspace.fs.writeFile(
            target, Buffer.from(dataUrl.slice(comma + 1), 'base64'));

        // Said out loud, and with a way to act on it: a file written somewhere the reader chose but
        // cannot see is a file they will look for in the wrong place.
        const open = 'Open';
        const chosen = await vscode.window.showInformationMessage(
            `Saved ${target.path.split('/').pop() ?? 'the capture'}.`, open);

        if (chosen === open) {
            await vscode.commands.executeCommand('vscode.open', target);
        }
    }

    /**
     * Opens the inspector on a row, and arranges to hear when the reader closes it.
     *
     * The preview marks the row it is inspecting, and the tab is its own editor - the reader can
     * close it from the tab bar, which the webview cannot see. So the close is reported back rather
     * than left for the mark to go stale on a tab that is no longer there.
     */
    private openInspector(subject: unknown): void {
        const alreadyOpen = ModelInspectorPanel.isOpen;
        const inspector = ModelInspectorPanel.show(this.extensionUri, this.lsp, subject);

        if (!alreadyOpen) {
            inspector.onDidDispose(() => this.post({ type: 'inspectorClosed' }));
        }
    }

    protected async onMessage(message: WebviewMessage): Promise<void> {
        switch (message.type) {
            case 'ready':
                // The room first, so the viewport is set up the way the reader left it before any
                // geometry lands - otherwise the grid and the lights visibly snap a frame later.
                this.post({
                    type: 'viewerSettings',
                    settings: readViewerSettings(),
                    // Beside the room rather than inside it: the two are at different TIERS -
                    // the room follows the person, these follow the project. See
                    // `projectSettingsStorage`.
                    project: readProjectSettings(),
                    subject: ModelPreviewPanel.subjectStates.get(this.stateKey) ?? null,
                });

                this.sendPreviewFeatures();

                // A preview is the ONE thing here VS Code opens by itself: a model tab left open
                // when the window was closed is restored on startup, and resolved long before the
                // server has finished starting. Asking straight away failed once, nothing asked
                // again, and the tab stayed blank until the file was closed and reopened by hand.
                // Waiting costs a fresh open nothing - it is already ready by then.
                await this.lsp.whenReady();
                await this.sendScene();
                return;

            case 'setViewerSettings':
                saveViewerSettings(message.settings);
                return;

            case 'setProjectSettings':
                saveProjectSettings(message.project);
                return;

            case 'setSubjectState':
                // Session-scoped and NOT persisted: a model's damage state, detail level and hidden
                // meshes describe that model, and restoring them across a restart would fight the
                // opening rules - a fresh preview must open undamaged, at full detail, and quiet.
                ModelPreviewPanel.subjectStates.set(
                    this.stateKey, subjectStateFrom(message.state));
                return;

            case 'saveCapture':
                await this.saveCapture(message);
                return;

            case 'requestGlb':
                await this.sendGlb(message);
                return;

            case 'requestModelDetail':
                await this.sendModelDetail(String(message.modelReference ?? ''));
                return;

            case 'requestSubMeshGeometry':
                this.post(await subMeshGeometryReply(this.lsp, message));
                return;

            case 'openInspector':
                this.openInspector(message.subject);
                return;

            case 'updateInspector':
                // Retargets a tab that is open and does nothing if none is. The preview sends this
                // whenever the row's facts change, so it must NOT be able to open one - a reader
                // who closed the tab would have it spring back the next time they ticked a box.
                ModelInspectorPanel.update(message.subject);
                return;

            case 'requestTexture':
                await this.sendTexture(String(message.name ?? ''));
                return;

            case 'requestParticleSystem':
                await this.sendParticleSystem(String(message.name ?? ''));
                return;

            case 'requestShader':
                await this.sendShader(String(message.name ?? ''));
                return;

            case 'requestProjectile':
                await this.sendProjectile(String(message.name ?? ''));
                return;

            case 'obtainShaders':
                // The setup flow already exists as a command; this only makes it reachable from the
                // place the reader finds out they need it, instead of the command palette alone.
                await vscode.commands.executeCommand('aet-eaw-edit.shaders.obtain');
                return;

            case 'revealDefinition':
                // A row in the panel holds a name and the type it should be - an ability's
                // GUI_Activated_Ability_Name, say - and nothing else. There is no document behind
                // it to run textDocument/definition against, because the server assembled the row
                // out of several files.
                await revealDefinition(
                    this.lsp,
                    String(message.value ?? ''),
                    typeof message.referenceType === 'string' ? message.referenceType : undefined);
                return;

            case 'openExternal':
                await this.openExternal(
                    message.tool === 'particles' ? PARTICLE_EDITOR : ALO_VIEWER);
                return;

            default:
                return;
        }
    }

    private async sendScene(refresh = false): Promise<void> {
        this.requestedTextures.clear();

        const result = await this.lsp.request<GetPreviewSceneResult>(
            'aet/getPreviewScene', requestFor(this.subject));

        if (!result.ok) {
            // Said out loud on an open, because the panel would otherwise sit empty with no reason
            // given. Kept quiet on a refresh: the model on screen is still the last good one, and a
            // failed background re-read is not worth a modal over the top of it.
            if (!refresh) {
                void vscode.window.showErrorMessage(
                    `EaWEdit: Could not build the preview scene. ${result.message}`);
            }
            return;
        }

        const serialised = JSON.stringify(result.value.scene);

        // Nothing about THIS subject moved, so nothing is sent. The notification cannot say which
        // previews an edit touches, so every open one asks - and without this, editing any file in
        // the workspace would throw away every part's geometry and fetch it all again.
        if (refresh && serialised === this.lastScene) {
            return;
        }

        this.lastScene = serialised;

        // Kept for the GLB request that follows: the scene is what knows which clips exist for this
        // model, and the geometry has to be baked with them or the picker has nothing to offer.
        this.sceneAnimations = result.value.scene.animations ?? [];

        // The reader's own state goes FIRST, exactly as it does on open: the webview holds it in a
        // ref and applies it after the scene handler has reset everything. Without it, a refresh
        // would put the damage state, the detail level and the chosen clip back to defaults every
        // time the file was saved.
        if (refresh) {
            this.post({
                type: 'viewerSettings',
                settings: readViewerSettings(),
                project: readProjectSettings(),
                subject: ModelPreviewPanel.subjectStates.get(this.stateKey) ?? null,
            });
        }

        this.post({ type: 'scene', scene: result.value.scene, refresh });
    }

    /**
     * Re-reads the tree behind every open preview.
     *
     * Called on <c>aet/previewSceneChanged</c>, which the server pushes once the new index is live -
     * so this reads the edit rather than racing it.
     */
    static refreshAll(): void {
        for (const panel of ModelPreviewPanel.live) {
            void panel.sendScene(true);
        }
    }

    private async sendGlb(message: WebviewMessage): Promise<void> {
        const modelReference = String(message.modelReference ?? '');

        // A PASSIVE subject names its own clips on the request: a death clone and a piece of
        // wreckage are their own models, and the scene's list describes the one that was opened.
        // Sending the subject's list for them worked only by the accident that a clone's model is
        // conventionally the hull's name plus a suffix, so its clip matched the hull's stem.
        // EMPTY means "nothing of its own", not "bake nothing". `??` only falls back on null, so
        // an empty list from an older server - or from a clone whose clips could not be enumerated -
        // asked for a GLB with no animation at all, and a death clone with no clip plays no death.
        // Before this field existed the scene's list was sent for everything and happened to carry
        // the clone's clip, so narrowing it without this guard was strictly worse.
        const own = Array.isArray(message.animations) && message.animations.length > 0
            ? message.animations.map(name => String(name))
            : null;

        const result = await this.lsp.request<GetModelGlbResult>('aet/getModelGlb', {
            modelReference,
            // An animation subject asks for its own clip. Anything else asks for every clip the
            // scene found for it - the picker needs them all, and the opening rules already say
            // none of them plays until the reader chooses one.
            animations: this.subject.kind === 'animation'
                ? [this.subject.animationReference]
                : own ?? this.sceneAnimations,
        });

        this.post({
            type: 'glb',
            partId: message.partId,
            attachToPartId: message.attachToPartId,
            attachBone: message.attachBone,
            result: result.ok
                ? result.value
                : { glb: null, animations: [], error: result.message },
        });
    }

    /**
     * What is inside a model, for the inspector.
     *
     * Asked for separately from the GLB and only when a panel wants it: a Star Destroyer's bone
     * matrices have no business in the load path of a preview nobody inspects.
     */
    private async sendModelDetail(modelReference: string): Promise<void> {
        const result = await this.lsp.request<GetModelDetailResult>(
            'aet/getModelDetail', { modelReference });

        this.post({
            type: 'modelDetail',
            modelReference,
            result: result.ok ? result.value : { detail: null, error: result.message },
        });
    }

    private async sendParticleSystem(name: string): Promise<void> {
        const result = await this.lsp.request<GetParticleSystemResult>(
            'aet/getParticleSystem', { name });

        this.post({
            type: 'particleSystem',
            name,
            result: result.ok ? result.value : { system: null, error: result.message },
        });
    }

    /**
     * One projectile's values, for "fill from a projectile".
     *
     * Not cached the way textures are: the reader picks one at a time and deliberately, and a stale
     * answer after an edit to the projectile's own XML would be worse than the round trip costs.
     */
    private async sendProjectile(name: string): Promise<void> {
        const result = await this.lsp.request<GetProjectileResult>(
            'aet/getProjectile', { name });

        this.post({
            type: 'projectile',
            name,
            result: result.ok ? result.value : { projectile: null, error: result.message },
        });
    }

    private async sendShader(name: string): Promise<void> {
        const result = await this.lsp.request<GetShaderSourceResult>(
            'aet/getShaderSource', { name });

        this.post({
            type: 'shader',
            name,
            result: result.ok
                ? result.value
                : { source: null, hasManagedShaders: false, error: result.message },
        });
    }

    private async sendTexture(name: string): Promise<void> {
        if (name === '' || this.requestedTextures.has(name.toLowerCase())) {
            return;
        }

        this.requestedTextures.add(name.toLowerCase());

        const result = await this.lsp.request<GetModelTextureResult>(
            'aet/getModelTexture', { name });

        this.post({
            type: 'texture',
            name,
            result: result.ok
                ? result.value
                : { format: null, data: null, error: result.message },
        });
    }

    /**
     * Hands the current subject to a standalone editor.
     *
     * The preview is read-only by design; these tools are where the editing happens. Following the
     * existing `modVerify.executable` convention, the path is a setting and nothing is guessed - the
     * extension does not search for an install.
     */
    private async openExternal(tool: ExternalTool): Promise<void> {
        const executable = vscode.workspace.getConfiguration().get<string>(tool.setting)?.trim();

        if (executable === undefined || executable === '') {
            const choice = await vscode.window.showWarningMessage(
                `EaWEdit: No path is configured for ${tool.label}.`, 'Open settings');

            if (choice === 'Open settings') {
                await vscode.commands.executeCommand(
                    'workbench.action.openSettings', tool.setting);
            }
            return;
        }

        const file = subjectFile(this.subject);
        if (file === null) {
            void vscode.window.showWarningMessage(
                `EaWEdit: ${tool.label} opens a file, and this preview was opened from a game object `
                + 'rather than a file.');
            return;
        }

        // A terminal rather than a detached process: the tool's own output stays visible, and a
        // failure to start is something the user can see and read.
        const terminal = vscode.window.createTerminal({ name: tool.label });
        terminal.sendText(`& "${executable}" "${file}"`, true);
        terminal.show(true);
    }
}

/** Identifies a panel, so the same subject reveals rather than opening a second one. */
function subjectKey(subject: PreviewSubject): string {
    switch (subject.kind) {
        case 'object': return `object:${subject.objectId}`;
        case 'model': return `model:${subject.modelReference}`;
        default: return `animation:${subject.animationReference}`;
    }
}

/** The request body for a subject. */
function requestFor(subject: PreviewSubject): Record<string, string> {
    switch (subject.kind) {
        case 'object': return { objectId: subject.objectId };
        case 'model': return { modelReference: subject.modelReference };
        default: return { animationReference: subject.animationReference };
    }
}

/** The file a subject came from, or null when it came from XML rather than a file. */
function subjectFile(subject: PreviewSubject): string | null {
    switch (subject.kind) {
        case 'model': return uriToPath(subject.modelReference);
        case 'animation': return uriToPath(subject.animationReference);
        default: return null;
    }
}

function uriToPath(reference: string): string | null {
    if (!reference.startsWith('file://')) {
        // A bare model name from the XML is not a path the external tool could open.
        return null;
    }

    return vscode.Uri.parse(reference).fsPath;
}
