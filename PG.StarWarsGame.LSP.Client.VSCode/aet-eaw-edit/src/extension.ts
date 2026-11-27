// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import { ChildProcess, spawn } from "child_process";
import * as fs from "fs";
import * as os from "os";
import * as l10n from "@vscode/l10n";
import * as vscode from "vscode";
import {
    ClientCapabilities,
    FeatureState,
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
    State,
    StaticFeature,
} from "vscode-languageclient/node";
import * as whatsNewCommands from "./commands/whatsNew";
import * as luaDebugCommands from "./commands/luaDebug";
import * as localisationCommands from "./commands/localisation";
import * as modBrowserCommands from "./commands/modBrowser";
import * as storyCommands from "./commands/story";
import * as projectCommands from "./commands/project";
import * as diagnosticsCommands from "./commands/diagnostics";
import * as navigationCommands from "./commands/navigation";
import * as previewCommands from "./commands/preview";
import * as gameObjectsCommands from "./commands/gameObjects";
import type { HostDeps } from "./commands/host";
import { CreditsPreviewPanel } from "./creditsPreviewPanel";
import { cfg, resolveFeatureFlags } from "./hostSettings";
import type { ProjectClient } from "./projectClient";
import { createEditWorkspace } from "./editWorkspace";
import {
    EFFECTIVE_SCHEME,
    parseEffectiveObjectQuery,
} from "./effectiveObjectUri";
import { EncyclopediaPanel } from "./encyclopediaPanel";
import { ModBrowserPanel } from "./modBrowserPanel";
import {
    ModBrowserOutlineItem,
    ModBrowserViewProvider,
} from "./modBrowserViewProvider";
import { PanelProject } from "./panelProject";
import { WebviewPanelHost } from "./webviewPanelHost";
import { WhatsNewPanel } from "./whatsNewPanel";
import { ModelPreviewEditorProvider } from "./modelPreviewEditor";
import { ModelInspectorPanel } from "./modelInspectorPanel";
import { ModelPreviewPanel } from "./modelPreviewPanel";
import { shaderDirectory } from "./shaderSources";
import {
    previewAssetsChanged,
    previewEffectsChanged,
    restartReason,
} from "./settingsRestart";
import { RestartCoordinator } from "./restartCoordinator";
import { type Stores, createStores } from "./stores";
import { LocalisationEditorPanel } from "./localisationEditorPanel";
import {
    LocalisationNavigatorViewProvider,
    LocTreeItem,
} from "./localisationNavigatorViewProvider";
import { logLine, setLogChannel, withoutHome } from "./log";
import { directoriesNeedingOwnWatcher } from "./dependencyWatchers";
import { LspGateway } from "./lsp/lspGateway";
import { RouteChanges, RoutedLspGateway } from "./lsp/routedLspGateway";
import { stopClient } from "./lsp/stopClient";
import { discoverProjects } from "./projectDiscovery";
import { isModFolder, projectOfferDue } from "./modLayout";
import {
    type ClientSpec,
    type LoadingPhase,
    clientSpecs,
    diagnosticCollectionNameFor,
    documentSelectorFor,
    ownerKey,
    ownsPath,
    phaseText,
    pickGag,
    planChanges,
    serverLogArgs,
    serverWorkDoneProgress,
    statusText,
} from "./projectClientPlan";
import { isInside, pathKey } from "./pathKey";
import {
    logFolderOf,
    logStoreOf,
    removeStaleLogFolders,
    workspaceIdOf,
} from "./logStore";
import { WatcherPool } from "./watcherPool";
import {
    type NavigatorProject,
    ProjectGroupedTree,
} from "./projectGroupedTree";
import { vscodeMessageSink } from "./lsp/vscodeMessageSink";
import {
    resolveServerCommand,
    resolveServerEnvironment,
} from "./serverCommand";
import { analyzerProblem, serverProblem } from "./serverLaunch";
import { gameDirectoryProblem } from "./gameDirectories";
import {
    AdoptModProjectParams,
    StoryGraphChangedParams,
    StorySimChangedParams,
} from "./protocol";
import { StoryGraphPanel } from "./storyGraphPanel";
import {
    StoryNavigatorViewProvider,
    StoryTreeItem,
} from "./storyNavigatorViewProvider";
import {
    acceptLabel,
    declineLabel,
    declinedMessage,
    diffTitle,
    outcomeOf,
    PgprojMigrationProposal,
    proposalMessage,
} from "./pgprojMigrationPrompt";
import {
    PreviewCacheBuildProgress,
    PreviewCacheBuildView,
    ShownProgress,
} from "./previewCacheBuildView";

// vscode-languageclient reads `<id>.trace.server` itself: when it sends initialize and on every
// change. Named so that is aet-eaw-edit.lsp.debug.trace.server, the handshake is traced at the level
// the setting asks for rather than only what follows it.
const CLIENT_ID = "aet-eaw-edit.lsp.debug";
const CLIENT_NAME = "Alamo Engine Tools - Empire at War Edit";
const REQUIRED_SERVER_VERSION = "0.5.2";

/**
 * Forces every language-feature capability to be advertised STATICALLY (in the initialize response)
 * rather than via dynamic `registerCapability`.
 * 6
 * Reporting `dynamicRegistration = false` for each capability makes the server emit it in the static
 * initialize response instead. Static capabilities are wired up deterministically at connect time,
 * identically on every run, immune to the batch-abort. `executeCommand` is intentionally left dynamic
 * (the extension owns those commands and forwards them via direct `sendRequest`, so its registration
 * failing is harmless).
 */
class ForceStaticCapabilitiesFeature implements StaticFeature {
    // Every textDocument client-capability that exposes a `dynamicRegistration` flag and that this
    // server actually provides. `synchronization` MUST be included - it is what replays `didOpen`.
    private static readonly capabilityKeys = [
        "synchronization",
        "completion",
        "hover",
        "signatureHelp",
        "declaration",
        "definition",
        "typeDefinition",
        "implementation",
        "references",
        "documentHighlight",
        "documentSymbol",
        "codeAction",
        "codeLens",
        "documentLink",
        "rename",
        "foldingRange",
        "selectionRange",
        "linkedEditingRange",
        "formatting",
        "rangeFormatting",
        "onTypeFormatting",
        "inlayHint",
        "semanticTokens",
    ] as const;

    fillClientCapabilities(capabilities: ClientCapabilities): void {
        const textDocument = (capabilities.textDocument ??= {}) as Record<
            string,
            { dynamicRegistration?: boolean }
        >;
        for (const key of ForceStaticCapabilitiesFeature.capabilityKeys) {
            const capability = (textDocument[key] ??= {});
            capability.dynamicRegistration = false;
        }
    }

    initialize(): void {
        // no-op: this feature only adjusts the advertised client capabilities.
    }

    getState(): FeatureState {
        return { kind: "static" };
    }

    clear(): void {
        // no-op
    }
}

/** URI scheme for the right-hand side of a project-migration diff: the file as it would be written. */
const PGPROJ_PROPOSED_SCHEME = "aet-pgproj-proposed";

/**
 * A leading XML comment banner clarifying that the document is a generated, read-only preview of the
 * fully-merged object - reinforcing what the tab title already says, for when only the body is visible.
 * ASCII only by house rule.
 */
function effectiveObjectBanner(objectId: string): string {
    return [
        "<!--",
        "  ============================================================",
        "  " + vscode.l10n.t("GENERATED READ-ONLY PREVIEW"),
        "  " +
            vscode.l10n.t(
                "Effective (fully-merged) form of variant object '{0}'.",
                objectId,
            ),
        "  " +
            vscode.l10n.t(
                "This is not an editable source file - edits are discarded, and",
            ),
        "  " + vscode.l10n.t("go-to-definition is not available here."),
        "  ============================================================",
        "-->",
        "",
    ].join("\n");
}

/**
 * Serves the proposed content of a project file so it can be the right-hand side of a diff.
 *
 * Held in memory and keyed by the project's path: the proposal is a one-off answer to one question,
 * and writing it to a temp file would leave something behind whichever way the user answers.
 */
class PgprojProposedContentProvider
    implements vscode.TextDocumentContentProvider
{
    private readonly _texts = new Map<string, string>();
    private readonly _onDidChange = new vscode.EventEmitter<vscode.Uri>();
    readonly onDidChange = this._onDidChange.event;

    /** Returns the URI to diff against, having stored what it should show. */
    offer(path: string, proposedText: string): vscode.Uri {
        const uri = vscode.Uri.parse(`${PGPROJ_PROPOSED_SCHEME}:${path}`);
        this._texts.set(uri.toString(), proposedText);
        this._onDidChange.fire(uri);
        return uri;
    }

    forget(uri: vscode.Uri): void {
        this._texts.delete(uri.toString());
    }

    provideTextDocumentContent(uri: vscode.Uri): string {
        return this._texts.get(uri.toString()) ?? "";
    }
}

/**
 * Serves the merged "effective" form of a variant GameObject as a read-only virtual XML document.
 * The object id and the project travel in the URI query (#276); content is fetched from that
 * project's server via `aet/getEffectiveObject`. Read-only is implicit for
 * TextDocumentContentProvider documents.
 */
class EffectiveObjectContentProvider
    implements vscode.TextDocumentContentProvider
{
    private readonly _onDidChange = new vscode.EventEmitter<vscode.Uri>();
    readonly onDidChange = this._onDidChange.event;

    /** Signals VS Code to re-fetch content for an already-open virtual document. */
    refresh(uri: vscode.Uri): void {
        this._onDidChange.fire(uri);
    }

    async provideTextDocumentContent(uri: vscode.Uri): Promise<string> {
        const { objectId, projectKey, typeName } = parseEffectiveObjectQuery(
            uri.query,
        );

        // The document IS the report here - it is read-only generated text, so a failure is written
        // into it as a comment rather than raised as a notification over the tab that just opened.
        const outcome = await gatewayFor(projectKey).request(
            "aet/getEffectiveObject",
            {
                objectId,
                typeName: typeName || undefined,
            },
        );

        if (!outcome.ok) {
            return outcome.reason === "offline"
                ? `<!-- ${l10n.t("EaWEdit: LSP server is not running.")} -->`
                : `<!-- ${l10n.t("EaWEdit: Failed to resolve effective object '{0}' - {1}", objectId, outcome.message)} -->`;
        }
        if (!outcome.value.found) {
            return `<!-- ${l10n.t("EaWEdit: No object named '{0}' was found in the workspace.", objectId)} -->`;
        }
        return effectiveObjectBanner(objectId) + outcome.value.xml;
    }
}

/** The running clients, in discovery order. */
const clients = new Map<string, ProjectClient>();

/** The first project in discovery order. */
function primaryClient(): ProjectClient | undefined {
    return clients.values().next().value;
}

/** Set first thing on activation: everything the extension remembers between sessions. */
let stores!: Stores;

/** Set on activation; the folder every server of this window writes its log file into. */
let serverLogFolder: string | undefined;

/** The project whose server owns a file, when one does (#230). */
function ownerOf(fsPath: string): ProjectClient | undefined {
    const key = ownerKey(
        [...clients.values()].map((p) => p.spec),
        fsPath,
    );
    return key === undefined ? undefined : clients.get(key);
}

/** The project of the document in front: a text editor's, or a custom editor's such as a model. */
function activeEditorProject(): ProjectClient | undefined {
    const input = vscode.window.tabGroups.activeTabGroup.activeTab?.input;
    const uri =
        vscode.window.activeTextEditor?.document.uri ??
        (input instanceof vscode.TabInputCustom ||
        input instanceof vscode.TabInputText
            ? input.uri
            : undefined);
    return uri?.scheme === "file" ? ownerOf(uri.fsPath) : undefined;
}

/**
 * The project a workspace folder stands for: the one owning the folder, else - for a folder holding
 * several projects - the active document's when it is inside the folder, else the first inside it.
 */
function folderProject(
    folder: vscode.WorkspaceFolder,
): ProjectClient | undefined {
    const owner = ownerOf(folder.uri.fsPath);
    if (owner !== undefined) {
        return owner;
    }
    const inside = [...clients.values()].filter((p) =>
        isInside(p.spec.workspaceRoot, folder.uri.fsPath),
    );
    const active = activeEditorProject();
    return active !== undefined && inside.includes(active) ? active : inside[0];
}

function chosenProject(): ProjectClient | undefined {
    const key = stores.chosenProject.read();
    return key === undefined ? undefined : clients.get(key);
}

/**
 * Where a request with no project of its own goes: the project of the document in front, else the
 * one last picked in this window, else the first.
 */
function routedProject(): ProjectClient | undefined {
    return activeEditorProject() ?? chosenProject() ?? primaryClient();
}

/**
 * Fired whenever the set of running servers changes. Every routed gateway is built on it, so none
 * can be left waiting for a route that is already back (#318).
 */
const routeChanges = new RouteChanges();

/**
 * Every request to the server goes through here.
 *
 * Resolves the client per call rather than holding it: clients are replaced on every restart and
 * come and go with the projects, so anything that captured one would keep talking to a dead
 * process. Built once at module scope because the panels and navigators are handed it at
 * construction and outlive any single client.
 */
const lsp = new RoutedLspGateway(
    () => routedProject()?.gateway,
    vscodeMessageSink,
    routeChanges,
);

/** Gateways bound to one project each, by key. */
const projectGateways = new Map<string, RoutedLspGateway>();

/**
 * Where a visual editor's composed edit is applied and saved. One for the window: an edit names
 * its files itself, and every project's client turns the protocol's edit into VS Code's the same
 * way, so whichever client runs converts it.
 */
const editWorkspace = createEditWorkspace(() => {
    for (const project of clients.values()) {
        if (project.client !== undefined) {
            return project.client.protocol2CodeConverter;
        }
    }
    return undefined;
});

/**
 * The gateway of a panel opened while no project runs: it binds to the one project that starts,
 * and with several it stays offline rather than follow the editor in front - a panel that did
 * showed one project's data through another's server, the shape #276 closed.
 */
const pendingProjectGateway = new RoutedLspGateway(
    () => (clients.size === 1 ? primaryClient()?.gateway : undefined),
    vscodeMessageSink,
    routeChanges,
);

/**
 * The key a request or a panel is for: the key given, or - for "", which the navigators use for
 * their one tree while the window holds a single project - that project's.
 */
function resolveProjectKey(key: string | undefined): string {
    if (key) {
        return key;
    }
    return clients.size === 1 ? (primaryClient()?.spec.key ?? "") : "";
}

/**
 * A gateway bound to one project for as long as the window runs it, across its restarts.
 *
 * A panel opened for a project keeps talking to that project's server whatever document is in
 * front later - with one project too, or a second project starting would re-route every panel
 * opened before it to whichever editor is in front (#276). The window's own routed gateway only
 * while no project runs.
 */
function gatewayFor(key: string | undefined): LspGateway {
    key = resolveProjectKey(key);
    if (!key) {
        return lsp;
    }
    let gateway = projectGateways.get(key);
    if (gateway === undefined) {
        gateway = new RoutedLspGateway(
            () => clients.get(key)?.gateway,
            vscodeMessageSink,
            routeChanges,
        );
        projectGateways.set(key, gateway);
    }
    return gateway;
}

/**
 * The project a panel opened for `key` belongs to, for its whole life (#276). Every panel gets its
 * project here and nowhere else, so none can be handed the routed gateway that follows the editor.
 */
function panelProject(key: string | undefined): PanelProject {
    const resolved = resolveProjectKey(key);
    return {
        key: resolved,
        name: clients.get(resolved)?.spec.name ?? "",
        gateway: resolved ? gatewayFor(resolved) : pendingProjectGateway,
        edits: editWorkspace,
        stores,
    };
}

/** Asks which project, and remembers the answer for this window. */
async function pickProject(): Promise<ProjectClient | undefined> {
    const picked = await vscode.window.showQuickPick(
        [...clients.values()].map((p) => ({
            label: p.spec.name,
            description: p.spec.projectPath,
            key: p.spec.key,
        })),
        {
            title: vscode.l10n.t("EaWEdit project"),
            placeHolder: vscode.l10n.t("Select a project"),
        },
    );
    if (picked === undefined) {
        return undefined;
    }
    stores.chosenProject.save(picked.key);
    return clients.get(picked.key);
}

/**
 * The key of the project a command acts on, "" with none running, null when the picker was
 * dismissed. Panels are filed under it, so two projects' panels of one name stay apart - and under
 * the real key with one project too, so a panel keeps its project when a second one starts.
 */
async function commandProjectKey(): Promise<string | null> {
    if (clients.size <= 1) {
        return resolveProjectKey("");
    }
    const project =
        activeEditorProject() ?? chosenProject() ?? (await pickProject());
    return project === undefined ? null : project.spec.key;
}

/**
 * The project a localisation command acts on: the asking panel's, else the clicked tree row's, else
 * the command's usual choice. A panel is not a text editor, so without its own key a command run
 * from it fell through to the remembered project or a pick (#276).
 */
async function locCommandProjectKey(arg: unknown): Promise<string | null> {
    const asked = (arg as { projectKey?: unknown } | undefined)?.projectKey;
    if (typeof asked === "string" && asked) {
        return asked;
    }
    const row =
        arg instanceof LocTreeItem
            ? localisationNavigatorProvider?.projectOf(arg)
            : undefined;
    return row ?? (await commandProjectKey());
}

let effectiveObjectProvider: EffectiveObjectContentProvider | undefined;
let pgprojProposedProvider: PgprojProposedContentProvider | undefined;
let localisationNavigatorProvider: ProjectGroupedTree<LocTreeItem> | undefined;
let storyNavigatorProvider: ProjectGroupedTree<StoryTreeItem> | undefined;
let modBrowserProvider: ProjectGroupedTree<ModBrowserOutlineItem> | undefined;

/**
 * The projects the navigators show: one per running server, or the window's one tree - under the
 * key "" - when there is a single project or none.
 */
function navigatorProjects(): NavigatorProject[] {
    return clients.size <= 1
        ? [{ key: "", name: "" }]
        : [...clients.values()].map((p) => ({
              key: p.spec.key,
              name: p.spec.name,
          }));
}

/**
 * The key the navigators file a project's tree under: "" while the window has one. Panels always
 * use the project's real key (#276).
 */
function viewKey(project: ProjectClient): string {
    return clients.size <= 1 ? "" : project.spec.key;
}
let statusItem: vscode.StatusBarItem | undefined;

/**
 * The globs the server reacts to on disk. Kept in one place because two watcher sets are built
 * from it: the workspace-folder ones VS Code resolves itself, and the per-directory ones below.
 * It mirrors the server's `WatchedGlobs`: the document kinds, then every extension the asset
 * catalog records, so a texture or a model dropped into the workspace re-globs the catalog instead
 * of waiting for a reload.
 */
const WATCHED_GLOBS = [
    "**/*.xml",
    "**/*.lua",
    "**/*.pgproj",
    "**/*.csv",
    "**/*.properties",
    "**/*.dat",
    "**/*.meg",
    "**/*.tga",
    "**/*.dds",
    "**/*.png",
    "**/*.alo",
    "**/*.ala",
    "**/*.wav",
    "**/*.mp3",
    "**/*.ted",
    "**/*.lc",
];

/**
 * One OS watcher per directory and glob, shared by every server that asked for it: two projects
 * referencing one dependency both need its changes.
 */
const dependencyWatcherPool = new WatcherPool((directory, glob, emit) => {
    const watcher = vscode.workspace.createFileSystemWatcher(
        new vscode.RelativePattern(vscode.Uri.file(directory), glob),
    );
    // 1 = Created, 2 = Changed, 3 = Deleted in the LSP FileChangeType enum.
    const subscriptions = [
        watcher.onDidCreate((uri) => emit({ path: uri.fsPath, type: 1 })),
        watcher.onDidChange((uri) => emit({ path: uri.fsPath, type: 2 })),
        watcher.onDidDelete((uri) => emit({ path: uri.fsPath, type: 3 })),
    ];
    return {
        dispose: () => {
            for (const subscription of subscriptions) {
                subscription.dispose();
            }
            watcher.dispose();
        },
    };
});

/**
 * Asks one server where its project's content lives and watches whatever the open folders do not
 * already cover. Safe to call repeatedly: the previous set is disposed first.
 */
async function refreshDependencyWatchers(
    project: ProjectClient,
): Promise<void> {
    const answer = await project.gateway.request("aet/getWatchDirectories");
    if (!answer.ok) {
        return;
    }

    const folders = (vscode.workspace.workspaceFolders ?? []).map(
        (f) => f.uri.fsPath,
    );
    const outside = directoriesNeedingOwnWatcher(
        answer.value.directories ?? [],
        folders,
    );

    for (const watcher of project.dependencyWatchers) {
        watcher.dispose();
    }
    project.dependencyWatchers = outside.map((directory) =>
        dependencyWatcherPool.acquire(directory, WATCHED_GLOBS, (event) =>
            forwardFileChange(project, vscode.Uri.file(event.path), event.type),
        ),
    );

    logLine(
        outside.length === 0
            ? `${projectLabel(project)}No project directories outside the workspace folders; no extra watchers needed.`
            : `${projectLabel(project)}Watching ${outside.length} project director(y/ies) outside the workspace: ${outside.join(", ")}`,
    );
}

function forwardFileChange(
    project: ProjectClient,
    uri: vscode.Uri,
    type: 1 | 2 | 3,
): void {
    void project.gateway.notify("workspace/didChangeWatchedFiles", {
        changes: [{ uri: uri.toString(), type }],
    });
}

/** A log line's prefix naming the project, when the window runs more than one. */
function projectLabel(project: ProjectClient): string {
    return project.spec.roots === undefined ? "" : `[${project.spec.name}] `;
}

/**
 * A cancellable progress notification that stays until `end` is called.
 *
 * `withProgress` hands its reporter over asynchronously, so what is reported before then is kept
 * and shown when it arrives.
 */
function showCancellableProgress(
    title: string,
    onCancel: () => void,
): ShownProgress {
    let reporter: vscode.Progress<{
        message?: string;
        increment?: number;
    }> | null = null;
    let finish: (() => void) | null = null;
    let ended = false;
    let latest: { message: string; fraction: number } | null = null;
    let reported = 0;

    const apply = (): void => {
        if (reporter === null || latest === null) {
            return;
        }
        reporter.report({
            message: latest.message,
            increment: (latest.fraction - reported) * 100,
        });
        reported = latest.fraction;
    };

    void vscode.window.withProgress(
        {
            location: vscode.ProgressLocation.Notification,
            title,
            cancellable: true,
        },
        (progress, token) => {
            token.onCancellationRequested(onCancel);
            reporter = progress;
            apply();
            return new Promise<void>((resolve) => {
                finish = resolve;
                if (ended) {
                    resolve();
                }
            });
        },
    );

    return {
        report(message, fraction) {
            latest = { message, fraction };
            apply();
        },
        end() {
            ended = true;
            finish?.();
        },
    };
}

let traceChannel: vscode.LogOutputChannel | undefined;
let log: vscode.OutputChannel | undefined;

/**
 * vscode-languageclient v10 types `traceOutputChannel` as `LogOutputChannel` and writes protocol
 * trace via its `trace()` method - which a real `LogOutputChannel` hides unless its (user-controlled)
 * log level is set to Trace, and which prefixes every line with a level/timestamp. Adapt a plain
 * `OutputChannel` to the `LogOutputChannel` shape so every level writes through unchanged, the JSON
 * verbatim.
 *
 * The library traces nothing below log level Trace, and reads the verbosity from the trace.server
 * setting above it - lifting `off` to `messages`. So the level stands for the setting's on/off: Off
 * when it is off, Trace otherwise, and the library is told when that changes.
 *
 * The traffic names files by their paths, so the channel writes the home directory as `~`.
 */
function createTraceChannel(name: string): vscode.LogOutputChannel {
    const channel = vscode.window.createOutputChannel(name);
    const logLevelEmitter = new vscode.EventEmitter<vscode.LogLevel>();
    const level = () =>
        cfg("lsp.debug").get<string>("trace.server", "off") === "off"
            ? vscode.LogLevel.Off
            : vscode.LogLevel.Trace;
    let current = level();
    const watch = vscode.workspace.onDidChangeConfiguration((e) => {
        if (!e.affectsConfiguration("aet-eaw-edit.lsp.debug.trace.server")) {
            return;
        }
        const next = level();
        if (next !== current) {
            current = next;
            logLevelEmitter.fire(next);
        }
    });
    const write = (message: string, ..._args: unknown[]): void =>
        channel.appendLine(message);
    const dispose = channel.dispose.bind(channel);
    return withoutHome(
        Object.assign(channel, {
            get logLevel() {
                return current;
            },
            onDidChangeLogLevel: logLevelEmitter.event,
            trace: write,
            debug: write,
            info: write,
            warn: write,
            error: write,
            dispose: () => {
                watch.dispose();
                logLevelEmitter.dispose();
                dispose();
            },
        }) as unknown as vscode.LogOutputChannel,
    );
}

/** A game install directory, from its `aet-eaw-edit.lsp.source` setting. */
function gameDirectory(
    name: "baseGameDirectory" | "expansionDirectory",
): string | undefined {
    return cfg("lsp.source").get<string>(name) || undefined;
}

function validateConfiguration(extensionPath: string): boolean {
    let valid = true;

    const serverMissing = serverProblem(
        resolveServerCommand(extensionPath, []),
        fs.existsSync,
    );
    if (serverMissing) {
        void vscode.window.showErrorMessage(serverMissing);
        valid = false;
    }

    const analyzerMissing = analyzerProblem(
        cfg("lua").get<string>("analyzerPath") ?? "",
        fs.existsSync,
    );
    if (analyzerMissing) {
        void vscode.window.showErrorMessage(
            vscode.l10n.t(
                'EaWEdit: Lua analyzer not found - {0}. Fix "aet-eaw-edit.lua.analyzerPath", or clear it to use the bundled one',
                analyzerMissing,
            ),
        );
        valid = false;
    }

    // A game directory that names no game is reported, and the server starts all the same: the
    // workspace's own files still resolve without it.
    for (const name of ["baseGameDirectory", "expansionDirectory"] as const) {
        const problem = gameDirectoryProblem(
            `aet-eaw-edit.lsp.source.${name}`,
            gameDirectory(name) ?? "",
            (folder) =>
                fs.existsSync(folder) && fs.statSync(folder).isDirectory(),
        );
        if (problem) {
            void vscode.window.showWarningMessage(problem);
        }
    }

    if (
        cfg("lsp.source.baseline").get<string>("type") === "local" &&
        !cfg("lsp.source.baseline").get<string>("localPath")
    ) {
        vscode.window.showErrorMessage(
            vscode.l10n.t(
                'EaWEdit: Baseline type is "local" but no path is configured. Set "aet-eaw-edit.lsp.source.baseline.localPath".',
            ),
        );
        valid = false;
    }

    if (
        cfg("lsp.schema").get<string>("source") === "local" &&
        !cfg("lsp.schema").get<string>("localPath")
    ) {
        vscode.window.showErrorMessage(
            vscode.l10n.t(
                'EaWEdit: Schema source is "local" but no directory is configured. Set "aet-eaw-edit.lsp.schema.localPath".',
            ),
        );
        valid = false;
    }

    return valid;
}

/**
 * Starts one project's client and server, registered under its key before it is running so the
 * status, the router and a stop all see it from the first moment.
 */
function startProjectClient(
    context: vscode.ExtensionContext,
    spec: ClientSpec,
): ProjectClient {
    const multi = spec.roots !== undefined;
    const project: ProjectClient = {
        spec,
        client: undefined,
        process: undefined,
        gateway: new LspGateway(
            () =>
                project.state === "failed" || project.state === "stopped"
                    ? undefined
                    : project.client,
            vscodeMessageSink,
        ),
        state: "starting",
        dependencyWatchers: [],
        owned: [],
    };
    clients.set(spec.key, project);
    const label = projectLabel(project);

    const waitForDebugger = cfg("lsp.debug").get<boolean>(
        "waitForDebugger",
        false,
    );
    const extraArgs: string[] = [];
    if (waitForDebugger) {
        extraArgs.push("--wait-for-debugger");
    }
    // Where the server's debug log goes: the workspace's folder in the user's own store, one file
    // per project. Without it the server writes beside its own binary, because a spawned process
    // inherits the EDITOR's working directory - one session's log landed inside the VS Code
    // installation folder. It was each project's own directory once, which put the log into the
    // mod's folder, its version control and whatever was packaged from it.
    if (serverLogFolder !== undefined) {
        // The file's level is an advanced setting: a release server writes no file unless it
        // names one, so a tester can be asked for a log without a Debug build.
        const fileLevel = cfg("lsp.debug").get<unknown>("fileLogLevel");
        extraArgs.push(
            ...serverLogArgs(
                spec,
                serverLogFolder,
                typeof fileLevel === "string" ? fileLevel : undefined,
            ),
        );
    }
    // How loud that log is. Only passed when it differs from the server's own default, so the
    // common case still starts with the command line it always had. Worth having as a setting
    // rather than a rebuild: the lines that say whether a document reached a sync handler are
    // Debug, so a session that publishes nothing cannot be diagnosed at the default level - and
    // reading one of those lines as absent, when the level suppresses it always, is how an
    // intermittent outage got a confident and wrong explanation.
    const logLevel = cfg("lsp.debug").get<string>("logLevel", "Information");
    if (logLevel && logLevel !== "Information") {
        extraArgs.push(`--log-level=${logLevel}`);
    }
    // The same resolution starts the Lua debug adapter (see luaDebugAdapterFactory), which is this
    // binary in a different mode; keeping it in one place is what keeps the two from drifting.
    const server = resolveServerCommand(context.extensionPath, extraArgs);
    logLine(`${label}Starting LSP server: ${server.description}`);
    const serverExe = server.command;
    const serverArgs = [...server.args];

    // Spawned here rather than handed to the client as {command, args} so that WE hold the handle.
    //
    // With the Executable form the library owns the process, and its only cleanup for a shutdown
    // that fails is `checkProcessDied` - a setTimeout(2000) that force-terminates if the process
    // is still alive. On deactivate the extension host exits long before that timer runs, so the
    // one case that strands a server is exactly the case the net does not cover. Holding the
    // ChildProcess ourselves is what makes an immediate kill possible; see stopClient.
    //
    // The library still pipes stderr to the output channel on this path, so the server's own
    // startup lines are not lost. It does NOT record the process (`_serverProcess` stays unset),
    // which is the trade: no library-side cleanup at all, and all of it ours.
    const serverOptions: ServerOptions = async () => {
        const child = spawn(serverExe, serverArgs, {
            stdio: ["pipe", "pipe", "pipe"],
            env: resolveServerEnvironment(),
        });
        project.process = child;
        // Only clear if this is still the current one - a restart may already have replaced it.
        child.on("exit", () => {
            if (project.process === child) {
                project.process = undefined;
            }
        });
        return child;
    };

    const schemaSource = cfg("lsp.schema").get<string>("source", "http");

    // Watchers for the files the server reacts to on DISK, as opposed to in an editor buffer:
    // dynamic enum sources, loose assets, .pgproj, and localisation text.
    //
    // These have to be created here. The server declares the same globs in
    // GameDidChangeWatchedFilesHandler.CreateRegistrationOptions, but that registration never
    // reached the client - a whole session's log showed didOpen/didChange/didSave/didClose arriving
    // and not one workspace/didChangeWatchedFiles - so every on-disk change was silently ignored.
    // That is why adding a value to a dynamic enum stayed "unknown" until the server was restarted,
    // and it equally affected asset, project-file and localisation reloads.
    //
    // Keep this list in step with the server's registration options.
    //
    // With several projects each server hears only the changes under its own directories, through
    // the shared workspace watchers (see forwardWorkspaceChange): a server re-indexes any changed
    // file it is told about, so one told about another project's files would index them as its own.
    const fileEvents = multi
        ? []
        : WATCHED_GLOBS.map((glob) =>
              vscode.workspace.createFileSystemWatcher(glob),
          );
    // The client disposes what it is given, but only on a clean shutdown; tying them to the
    // extension's own lifetime means a failed start cannot leak OS watchers.
    context.subscriptions.push(...fileEvents);

    // One project writes its protocol trace where it always did; several each get their own.
    const trace = multi
        ? createTraceChannel(`EaWEdit LSP Trace (${spec.name})`)
        : traceChannel;
    if (multi && trace) {
        project.owned.push(trace);
    }

    // The server's channel, made here rather than by the client library: the library writes the
    // server's log messages and its error stream into it, and they name paths under the home
    // directory - a temporary folder, the log folder, a project kept in the user's documents.
    const clientName = multi ? `${CLIENT_NAME} (${spec.name})` : CLIENT_NAME;
    const serverChannel = withoutHome(
        vscode.window.createOutputChannel(clientName, { log: true }),
    );
    project.owned.push(serverChannel);

    const clientOptions: LanguageClientOptions = {
        synchronize: multi ? {} : { fileEvents },
        // Story-dialog scripts open as plaintext; the server gates all work on the pgproj
        // storyDialog registry scope, so other .txt files stay untouched.
        documentSelector: documentSelectorFor(spec),
        diagnosticCollectionName: diagnosticCollectionNameFor(CLIENT_ID, spec),
        outputChannel: serverChannel,
        traceOutputChannel: trace,
        // The status item above is the one footer for every project; the server's startup progress
        // would open a second beside it.
        middleware: {
            handleWorkDoneProgress: (token, params, next) =>
                serverWorkDoneProgress(params, () => next(token, params)),
        },
        initializationOptions: {
            workspaceRoot: spec.workspaceRoot,
            projectPath: spec.projectPath,
            baseGamePath: gameDirectory("baseGameDirectory"),
            // The user's own copy of the base shader SOURCES; never shipped with this extension.
            shaderPath: shaderDirectory(),
            expansionGamePath: gameDirectory("expansionDirectory"),
            // The game's language, not this extension's: it picks the string table.
            localisationLanguage: cfg("lsp.localisation").get<string>(
                "language",
                "ENGLISH",
            ),
            schemaUrl:
                schemaSource === "http"
                    ? cfg("lsp.schema").get<string>("url") || undefined
                    : undefined,
            schemaLocalPath:
                schemaSource === "local"
                    ? cfg("lsp.schema").get<string>("localPath") || undefined
                    : undefined,
            baselineType: cfg("lsp.source.baseline").get<string>(
                "type",
                "http",
            ),
            baselineUrl:
                cfg("lsp.source.baseline").get<string>("type", "http") ===
                "http"
                    ? cfg("lsp.source.baseline").get<string>("url") || undefined
                    : undefined,
            baselineLocalPath:
                cfg("lsp.source.baseline").get<string>("localPath") ||
                undefined,
            // Off by default and deliberately so: on the base game the startup sweep is 18,281
            // diagnostics and 21.8 MiB, and a mod is larger. It is the escape hatch for a start
            // where didOpen never reaches the server, not a normal mode.
            diagnostics: {
                workspaceOnStartup: cfg("diagnostics").get<boolean>(
                    "workspaceOnStartup",
                    false,
                ),
            },
            features: resolveFeatureFlags(),
            // A mod folder about to be offered a project file: the offer says what is missing.
            projectFileOffered:
                spec.projectPath === undefined &&
                projectOfferDue(
                    spec.workspaceRoot,
                    subdirectories,
                    files,
                    askedAboutProject(),
                ),
        },
    };

    // The client's name is its output channel's: one per project when there are several.
    const lspClient = new LanguageClient(
        CLIENT_ID,
        clientName,
        serverOptions,
        clientOptions,
    );
    project.client = lspClient;

    // Must be registered before start(): forces every language-feature capability to be advertised
    // statically, so registration is deterministic and immune to the dynamic-batch abort (and so
    // already-open documents get a didOpen). Otherwise a random subset of features may fail.
    lspClient.registerFeature(new ForceStaticCapabilitiesFeature());

    updateStatus();

    lspClient
        .start()
        .then(async () => {
            logLine(`${label}LSP server started and initialized.`);

            // The handshake is done and the server is answering; what remains is the workspace scan,
            // which runs in the background and can take half a minute on a large mod. Saying so is the
            // whole point of splitting these two states: the status used to read "Starting..." from
            // before start() until $/workspaceScanComplete, so a 30-second scan was indistinguishable
            // from a server that never came up - which is exactly how it was reported.
            // A scan-complete that raced the handshake has already moved this on.
            if (project.state === "starting") {
                project.state = "indexing";
            }
            updateStatus();

            // Anything already waiting on a server can go now. A model preview restored when the window
            // opened is resolved by VS Code long before this point, and without being told it would sit
            // blank for ever on the one request it made and lost.
            project.gateway.markReady();

            const serverVersion =
                lspClient.initializeResult?.serverInfo?.version;
            logLine(
                `${label}Server version reported: ${serverVersion ?? "(none)"}`,
            );
            // The bundled server is this release's by construction; only a configured one can differ.
            // Every project runs the same executable, so the first project's server speaks for all.
            if (
                !server.bundled &&
                serverVersion !== REQUIRED_SERVER_VERSION &&
                project === primaryClient()
            ) {
                void vscode.window.showWarningMessage(
                    serverVersion
                        ? vscode.l10n.t(
                              'EaWEdit: Server version {0} does not match the expected version {1}. Clear "aet-eaw-edit.lsp.executable" to use the bundled server',
                              serverVersion,
                              REQUIRED_SERVER_VERSION,
                          )
                        : vscode.l10n.t(
                              'EaWEdit: The server did not report a version. Clear "aet-eaw-edit.lsp.executable" to use the bundled server',
                          ),
                );
            }
        })
        .catch((e: unknown) => {
            // Stopped on purpose while starting - the project went, or the window restarts it.
            if (
                clients.get(spec.key) !== project ||
                project.state === "stopped"
            ) {
                return;
            }
            logLine(`${label}LSP server failed to start: ${e}`);
            project.state = "failed";
            project.gateway.markStopped();
            updateStatus();
            void vscode.window
                .showErrorMessage(
                    vscode.l10n.t(
                        "EaWEdit: LSP server failed to start. Check the EaWEdit output channel for details.",
                    ),
                    vscode.l10n.t("Show Output"),
                )
                .then((choice) => {
                    if (choice === vscode.l10n.t("Show Output")) {
                        log?.show(true);
                    }
                });
        });

    // The server has read a project file in an older format and wants to write the updated form.
    // It asks rather than tells, and shows rather than describes: the user gets the two files side
    // by side and decides. Declining stops the server - see declinedMessage.
    lspClient.onRequest(
        "aet/pgprojMigrationProposal",
        async (
            proposal: PgprojMigrationProposal,
        ): Promise<{ accepted: boolean }> => {
            const proposedUri = pgprojProposedProvider?.offer(
                proposal.path,
                proposal.proposedText,
            );
            if (proposedUri) {
                await vscode.commands.executeCommand(
                    "vscode.diff",
                    vscode.Uri.file(proposal.path),
                    proposedUri,
                    diffTitle(proposal.fileName),
                    { preview: false } satisfies vscode.TextDocumentShowOptions,
                );
            }

            const answer = await vscode.window.showInformationMessage(
                proposalMessage(proposal),
                { modal: true },
                acceptLabel(),
                declineLabel(),
            );
            const outcome = outcomeOf(answer);
            if (proposedUri) {
                pgprojProposedProvider?.forget(proposedUri);
            }

            if (outcome === "accept") {
                return { accepted: true };
            }

            // Stopped from HERE rather than by the server exiting: vscode-languageclient treats a
            // server that exits on its own as a crash and restarts it, straight back into this same
            // proposal. The client stopping itself is the only version that stays stopped.
            // Only this project's server stops; the others are not asked about this file.
            logLine(
                `${label}Project migration declined - stopping the language server.`,
            );
            project.state = "stopped";
            project.gateway.markStopped();
            updateStatus();
            void lspClient.stop().then(() => {
                void vscode.window.showWarningMessage(
                    declinedMessage(proposal.fileName),
                );
            });
            return { accepted: false };
        },
    );

    lspClient.onNotification("$/workspaceScanComplete", async () => {
        logLine(`${label}Workspace scan complete.`);
        // Only now does the server know which projects the workspace resolved to, so this is the
        // first moment the dependency directories can be asked for. The notification fires on
        // startup only, which is why a .pgproj change rebuilds them separately.
        void refreshDependencyWatchers(project);
        // The index is open, but the views draw from feeds the server builds on request: the
        // campaigns, the translations, the Mod Browser's lists. The bar used to show its check
        // mark here while all three still said they were loading - two minutes apart on a large
        // mod. The project is "loading" until each has answered once, in this order, and the bar
        // names the one it waits for. A tree view only asks for its children on reveal, so this is
        // also what spares the first click on a navigator its round trip.
        const phases: [LoadingPhase, () => Promise<void> | undefined][] = [
            [
                "campaigns",
                () => storyNavigatorProvider?.preload(viewKey(project)),
            ],
            [
                "translations",
                () => localisationNavigatorProvider?.preload(viewKey(project)),
            ],
            ["assets", () => modBrowserProvider?.preload(viewKey(project))],
        ];
        for (const [phase, load] of phases) {
            project.state = "loading";
            project.phase = phase;
            // Now and then a phase shows a line from elsewhere in the galaxy instead of its own
            // name: one draw as it starts, and a hit stands for the whole phase. Phases last
            // seconds now, so a line shown for a moment and taken back was never read.
            project.gag = pickGag(Math.random);
            updateStatus();
            try {
                await load();
            } catch (e) {
                logLine(`${label}Loading ${phase} failed: ${e}`);
            }
        }
        // A reload or a stop that happened meanwhile owns the state now.
        if (project.state !== "loading") {
            return;
        }
        project.phase = undefined;
        project.gag = undefined;
        project.state = "ready";
        updateStatus();
        // An encyclopedia card opened during the scan was answered from the baseline alone and kept
        // that answer: measured on EaWX, the popup width came back as vanilla's 262 before the scan
        // and the mod's own 340 after it. Nothing re-asked, so the card stayed vanilla all session.
        EncyclopediaPanel.refresh(project.spec.key);
        // A browser opened during the scan listed the baseline alone.
        ModBrowserPanel.refresh(project.spec.key);
    });

    lspClient.onNotification("aet/localisationIndexUpdated", () => {
        logLine("Localisation index updated - refreshing localisation views.");
        localisationNavigatorProvider?.refresh(viewKey(project));
        // Open tabs decide for themselves whether to re-read: one with staged edits must not be
        // reloaded out from under the user. The notification fires on our own saves too.
        LocalisationEditorPanel.invalidateAll(project.spec.key);
    });

    lspClient.onNotification(
        "aet/storyGraphChanged",
        (params: StoryGraphChangedParams) => {
            logLine(
                `Story graph changed: ${params.campaigns.join(", ")} - refreshing story views.`,
            );
            storyNavigatorProvider?.refresh(viewKey(project));
            StoryGraphPanel.refreshInvalidated(
                params.campaigns ?? [],
                project.spec.key,
            );
        },
    );

    lspClient.onNotification(
        "aet/storySimChanged",
        (params: StorySimChangedParams) => {
            StoryGraphPanel.simChanged(
                {
                    campaign: params.campaign,
                    faction: params.faction,
                    scope: params.scope ?? undefined,
                },
                project.spec.key,
            );
        },
    );

    lspClient.onNotification("aet/previewSceneChanged", () => {
        // Every open preview asks; the ones whose scene did not actually change drop the answer.
        // The server cannot say which previews an edit touches - a scene is assembled from a
        // variant chain, its hardpoints, their models and their weapons, and none of that is
        // tracked back to the files it came from.
        ModelPreviewPanel.refreshAll(project.spec.key);
    });

    lspClient.onNotification("aet/previewAssetsChanged", () => {
        // A model, a clip or a texture changed on disk. The scene may read exactly as before, so
        // the previews are told to ask for their assets again; each says which version it holds
        // and is sent only what changed.
        ModelPreviewPanel.refreshAll(project.spec.key, true);
    });

    // The background build of this project's preview cache: one progress with a Cancel button for
    // as long as the server says it is running. A notification rather than the status bar, because
    // only there can a progress be cancelled.
    const cacheBuild = new PreviewCacheBuildView(
        (onCancel) =>
            showCancellableProgress(
                projectLabel(project) +
                    vscode.l10n.t("Converting models for the preview"),
                onCancel,
            ),
        () => {
            void project.gateway.request("aet/cancelPreviewCacheBuild", {});
        },
        (done, total) => vscode.l10n.t("{0} of {1} models", done, total),
    );
    lspClient.onNotification(
        "aet/previewCacheBuild",
        (progress: PreviewCacheBuildProgress) => cacheBuild.update(progress),
    );
    lspClient.onDidChangeState((event) => {
        // A server that stopped will send no "ended".
        if (event.newState === State.Stopped) {
            cacheBuild.end();
        }
    });

    lspClient.onNotification("aet/modBrowserChanged", () => {
        // The index moved - an edit, a reload, a texture dropped into the workspace - so every
        // open browser of this project re-reads its counts, its rail and its table. Until this
        // notification existed the browser refreshed on the scan-complete signal alone, and a new
        // asset stayed out of it until a sort or a node switch happened to ask again.
        ModBrowserPanel.refresh(project.spec.key);
    });

    return project;
}

async function stopProjectClient(project: ProjectClient): Promise<void> {
    // The reference goes first, and on every path. `LanguageClient.stop()` throws when its
    // shutdown handshake loses the race with its own timer, and the old shape - stop, then clear -
    // meant a timeout skipped both the clear and the restart that three of the four callers do
    // next. The visible symptom was the language server never coming back after a feature-flag
    // change, with nothing in the log but "Stopping the server timed out".
    const client = project.client;
    const child = project.process;
    project.client = undefined;
    project.process = undefined;
    project.state = "stopped";
    for (const watcher of project.dependencyWatchers) {
        watcher.dispose();
    }
    project.dependencyWatchers = [];
    try {
        await stopClient(client, logLine, {
            forceKill: () => killServer(child),
        });
    } finally {
        project.gateway.markStopped();
        for (const owned of project.owned) {
            owned.dispose();
        }
        project.owned = [];
    }
}

/** Stops every project's server; the window has none running afterwards. */
async function stopAllClients(): Promise<void> {
    const running = [...clients.values()];
    clients.clear();
    routeChanges.fire();
    WebviewPanelHost.setProjectCount(clients.size);
    await Promise.all(running.map((project) => stopProjectClient(project)));
    // Says when the stop is over, not only when it began, so a start that overlapped it shows.
    logLine("All LSP servers stopped.");
}

/**
 * The status bar: one server reads as the single server always has, several count the projects
 * indexed and list each one in the tooltip.
 */
function updateStatus(): void {
    if (!statusItem) {
        return;
    }
    const running = [...clients.values()];
    if (running.length === 0) {
        return;
    }

    statusItem.text = statusText(
        running.map((p) => ({
            name: p.spec.name,
            state: p.state,
            phase: p.phase,
            gag: p.gag,
        })),
    );
    if (running.length === 1) {
        const state = running[0].state;
        if (state === "indexing" || state === "loading") {
            statusItem.tooltip = vscode.l10n.t(
                "The server is connected. Language features and the views become complete when loading finishes.",
            );
        } else if (state === "ready") {
            statusItem.tooltip = undefined;
        }
    } else {
        statusItem.tooltip = running
            .map(
                (p) =>
                    `${p.spec.name} - ${phaseText({ state: p.state, phase: p.phase }) ?? p.state}`,
            )
            .join("\n");
    }
    statusItem.show();
}

/**
 * Serialises discovery runs. Every caller but the debug Force Startup command reaches this through
 * {@link restarts}, which already runs one thing at a time; this keeps that command's run from
 * overlapping one of its.
 */
let reconciling: Promise<void> = Promise.resolve();

/**
 * The one queue every restart and every project discovery runs in (#395), so a discovery and a
 * settings restart never stop or start clients at the same time. Set as activation begins.
 */
let restarts: RestartCoordinator | undefined;

/**
 * Brings the servers in line with the window's projects, in the queue restarts run in: starts the
 * projects that appeared, stops the ones that went, leaves the rest.
 */
function rediscover(): Promise<void> {
    return restarts?.request({ causes: ["discovery"] }) ?? Promise.resolve();
}

/**
 * Discovers the window's projects and brings the running servers in line: starts the projects that
 * appeared, stops the ones that went, restarts the ones whose directories changed, and leaves every
 * other server alone.
 */
function reconcileClients(context: vscode.ExtensionContext): Promise<void> {
    reconciling = reconciling
        .then(() => reconcileNow(context))
        .catch((e: unknown) => logLine(`Project discovery failed: ${e}`));
    return reconciling;
}

async function reconcileNow(context: vscode.ExtensionContext): Promise<void> {
    const folders = (vscode.workspace.workspaceFolders ?? []).map(
        (f) => f.uri.fsPath,
    );
    const found = await vscode.workspace.findFiles(
        "**/*.pgproj",
        "**/{node_modules,.git}/**",
    );
    const discovery = discoverProjects({
        folders,
        projectFiles: found.map((uri) => uri.fsPath),
        read: (file) => {
            try {
                return fs.readFileSync(file, "utf8");
            } catch {
                return undefined;
            }
        },
    });
    const specs = clientSpecs(
        discovery,
        folders,
        onDiskSpelling,
        (folder) => isModFolder(folder, subdirectories, files),
        subdirectories,
    );

    if (specs.length > 1) {
        logLine(
            `Projects: ${specs.map((s) => s.name).join(", ")}` +
                (discovery.nested.length > 0
                    ? `; served as a dependency of the project around them: ${discovery.nested.join(", ")}`
                    : "") +
                (discovery.foldersWithoutProject.length > 0
                    ? `; folders without a project: ${discovery.foldersWithoutProject.join(", ")}`
                    : ""),
        );
    }

    const running = new Map(
        [...clients].map(([key, project]) => [key, project.spec.signature]),
    );
    const plan = planChanges(running, specs);
    for (const key of plan.stop) {
        const project = clients.get(key);
        clients.delete(key);
        if (project) {
            await stopProjectClient(project);
        }
    }
    for (const spec of plan.start) {
        startProjectClient(context, spec);
    }

    // Discovery order is routing order, so the map follows the specs.
    const ordered = specs
        .map((s) => [s.key, clients.get(s.key)] as const)
        .filter((entry): entry is [string, ProjectClient] => !!entry[1]);
    clients.clear();
    for (const [key, project] of ordered) {
        clients.set(key, project);
    }
    routeChanges.fire();
    WebviewPanelHost.setProjectCount(clients.size);
    storyNavigatorProvider?.projectsChanged();
    localisationNavigatorProvider?.projectsChanged();
    modBrowserProvider?.projectsChanged();
    updateStatus();
    if (discovery.projects.length === 0 && folders.length > 0) {
        void offerProjectFile(folders[0]);
    }
}

/** Folders offered a project file this session, so a rediscovery does not ask again. */
const projectOffered = new Set<string>();

/** The folders already asked about a project file: declined for good, or offered this session. */
function askedAboutProject(): string[] {
    return [...stores.projectOfferDeclined.read(), ...projectOffered];
}

/**
 * Offers a project file for a mod opened without one (#295), once: the server writes it from what
 * the folder holds and it opens, so what was guessed is in front of the reader. Its creation
 * restarts the servers like any new `.pgproj`.
 */
async function offerProjectFile(folder: string): Promise<void> {
    const key = pathKey(folder);
    if (!projectOfferDue(folder, subdirectories, files, askedAboutProject())) {
        return;
    }
    projectOffered.add(key);

    const create = vscode.l10n.t("Create project file");
    const notNow = vscode.l10n.t("Not now");
    const never = vscode.l10n.t("Not for this folder");
    const choice = await vscode.window.showInformationMessage(
        vscode.l10n.t("EaWEdit: Mod without a project file - {0}", folder),
        create,
        notNow,
        never,
    );
    if (choice === never) {
        stores.projectOfferDeclined.save([
            ...stores.projectOfferDeclined.read(),
            key,
        ]);
        return;
    }
    if (choice !== create) {
        return;
    }

    const outcome = await lsp.request("aet/adoptModProject", {
        directory: folder,
    } satisfies AdoptModProjectParams);
    if (!outcome.ok) {
        void vscode.window.showErrorMessage(
            vscode.l10n.t(
                "EaWEdit: Project file not created - {0}",
                outcome.message,
            ),
        );
        return;
    }
    if (outcome.value.projectFile === null) {
        void vscode.window.showWarningMessage(
            vscode.l10n.t(
                "EaWEdit: Project file not created - {0}",
                outcome.value.error ?? "",
            ),
        );
        return;
    }
    await vscode.window.showTextDocument(
        vscode.Uri.file(outcome.value.projectFile),
    );
}

/** A directory as the disk spells it, so a document selector matches VS Code's spelling. */
function onDiskSpelling(directory: string): string {
    try {
        return fs.realpathSync.native(directory).replace(/\\/g, "/");
    } catch {
        return directory;
    }
}

/** The subdirectories of a directory, or undefined when it cannot be read. */
function subdirectories(directory: string): string[] | undefined {
    try {
        return fs
            .readdirSync(directory, { withFileTypes: true })
            .filter((entry) => entry.isDirectory())
            .map((entry) => entry.name);
    } catch {
        return undefined;
    }
}

/** The files of a directory, or undefined when it cannot be read. */
function files(directory: string): string[] | undefined {
    try {
        return fs
            .readdirSync(directory, { withFileTypes: true })
            .filter((entry) => entry.isFile())
            .map((entry) => entry.name);
    } catch {
        return undefined;
    }
}

/**
 * The window's on-disk watchers, shared by every server when there are several.
 *
 * A project file created or deleted changes which servers run; any project file change may move the
 * directories a server watches outside the folders. With several projects, every other change goes
 * to the servers whose directories hold it - one server hearing another project's file would index
 * it as its own.
 */
function watchWorkspace(context: vscode.ExtensionContext): void {
    for (const glob of WATCHED_GLOBS) {
        const watcher = vscode.workspace.createFileSystemWatcher(glob);
        const handle = (uri: vscode.Uri, type: 1 | 2 | 3) => {
            if (uri.fsPath.toLowerCase().endsWith(".pgproj")) {
                onProjectFileTouched();
            }
            forwardWorkspaceChange(uri, type);
        };
        context.subscriptions.push(
            watcher,
            watcher.onDidCreate((uri) => handle(uri, 1)),
            watcher.onDidChange((uri) => handle(uri, 2)),
            watcher.onDidDelete((uri) => handle(uri, 3)),
        );
    }
}

function onProjectFileTouched(): void {
    // Not validated here: the configuration was checked when the servers started, and a project
    // file saved on every keystroke must not raise the same settings errors each time.
    void rediscover().then(() => {
        // A .pgproj change can add or drop a project reference, which moves the very directories the
        // dependency watchers cover - so rebuild them whenever one changes, wherever it lives.
        for (const project of clients.values()) {
            void refreshDependencyWatchers(project);
        }
    });
}

/** Several projects: a change goes to the servers whose directories hold it. One: its client syncs. */
function forwardWorkspaceChange(uri: vscode.Uri, type: 1 | 2 | 3): void {
    for (const project of clients.values()) {
        if (
            project.spec.watched !== undefined &&
            ownsPath(project.spec, uri.fsPath)
        ) {
            forwardFileChange(project, uri, type);
        }
    }
}

/**
 * Terminates a server that would not shut down.
 *
 * SIGKILL rather than SIGTERM: this only runs after the polite request has already failed, and on
 * Windows node maps both to TerminateProcess anyway, so asking twice would only be slower. A
 * process that has already exited is left alone - `exitCode`/`signalCode` are set the moment it
 * goes, and killing a reaped pid is how you end up signalling whatever reused the number.
 */
function killServer(child: ChildProcess | undefined): void {
    if (
        child === undefined ||
        child.exitCode !== null ||
        child.signalCode !== null
    ) {
        return;
    }
    logLine(`Killing LSP server process ${child.pid ?? "(no pid)"}.`);
    child.kill("SIGKILL");
}

export async function activate(
    context: vscode.ExtensionContext,
): Promise<void> {
    // The modules that cannot import `vscode` translate through `@vscode/l10n`; they read the same
    // bundle VS Code loaded for the display language. English ships no bundle: the texts are the keys.
    if (vscode.l10n.bundle !== undefined) {
        l10n.config({ contents: vscode.l10n.bundle });
    }

    // Before anything reads a stored value. Which value follows the person and which the mod is
    // decided in `stores`.
    stores = createStores(context.globalState, context.workspaceState);

    localisationNavigatorProvider = new ProjectGroupedTree<LocTreeItem>(
        navigatorProjects,
        (key) => new LocalisationNavigatorViewProvider(gatewayFor(key)),
    );

    effectiveObjectProvider = new EffectiveObjectContentProvider();
    pgprojProposedProvider = new PgprojProposedContentProvider();
    context.subscriptions.push(
        vscode.workspace.registerTextDocumentContentProvider(
            EFFECTIVE_SCHEME,
            effectiveObjectProvider,
        ),
        vscode.workspace.registerTextDocumentContentProvider(
            PGPROJ_PROPOSED_SCHEME,
            pgprojProposedProvider,
        ),
    );

    modBrowserProvider = new ProjectGroupedTree<ModBrowserOutlineItem>(
        navigatorProjects,
        (key) => new ModBrowserViewProvider(gatewayFor(key)),
    );

    storyNavigatorProvider = new ProjectGroupedTree<StoryTreeItem>(
        navigatorProjects,
        (key) => new StoryNavigatorViewProvider(gatewayFor(key)),
    );

    statusItem = vscode.window.createStatusBarItem(
        vscode.StatusBarAlignment.Left,
        0,
    );
    statusItem.tooltip = CLIENT_NAME;
    // The one place every state of the server is shown, so it is where a report starts.
    statusItem.command = {
        command: "aet-eaw-edit.copyBugReportInfo",
        title: vscode.l10n.t("Copy bug report info"),
    };
    context.subscriptions.push(statusItem);

    log = withoutHome(vscode.window.createOutputChannel("EaWEdit"));
    setLogChannel(log);
    context.subscriptions.push(log);

    traceChannel = createTraceChannel("EaWEdit LSP Trace");
    context.subscriptions.push(traceChannel);

    logLine("Extension activated.");
    logLine(
        `  lsp.devMode      = ${cfg("lsp.devMode").get<boolean>("enabled", false)}`,
    );
    logLine(
        `  lsp.executable   = ${cfg("lsp").get<string>("executable") || "(bundled)"}`,
    );
    logLine(
        `  lsp.projectPath  = ${cfg("lsp.devMode").get<string>("projectPath") ?? "(not set)"}`,
    );

    // One log folder per workspace in the user's own store, named by the id VS Code files this
    // workspace's storage under. Set before the first server starts; a workspace that is gone
    // would leave its folder behind for good, so the ones long unused go here.
    const workspaceId = workspaceIdOf(context.storageUri?.fsPath);
    serverLogFolder = logFolderOf(os.homedir(), workspaceId);
    for (const removed of removeStaleLogFolders(
        logStoreOf(os.homedir()),
        workspaceId,
        new Date(),
    )) {
        logLine(`Removed the unused log folder ${removed}.`);
    }

    // Every restart and every project discovery goes through one coordinator: a configuration
    // change, the Restart command, a start with no server running, a workspace folder or a project
    // file coming or going. Set before the watchers below can fire. See restartCoordinator.ts for
    // the rules it keeps.
    restarts = new RestartCoordinator({
        running: () => clients.size > 0,
        validate: () => validateConfiguration(context.extensionPath),
        stop: () => stopAllClients(),
        start: () => reconcileClients(context),
        log: logLine,
        refresh: (effects, assets) => {
            // The webviews hold what the old folders answered, a missing effect included; they
            // ask again, and the gateway holds each request until the new server is ready.
            if (effects) {
                ModelPreviewPanel.refetchEffectsAll();
            }

            // Models and textures resolve through the game folders. A scene refresh sends
            // nothing when the scene reads as before, and from one set game folder to another
            // it does: SET-32 measured nothing asked for again. So every preview is refreshed
            // as if its assets changed on disk, through the path that notification takes.
            if (assets) {
                for (const key of clients.keys()) {
                    ModelPreviewPanel.refreshAll(key, true);
                }
            }
        },
    });
    const coordinator = restarts;

    watchWorkspace(context);
    // A folder added or removed changes which projects the window holds; only the servers of the
    // projects that came or went start or stop.
    context.subscriptions.push(
        vscode.workspace.onDidChangeWorkspaceFolders(() => {
            // A server kept running may now find a dependency inside a folder, or outside one:
            // its own watchers over directories outside the folders are rebuilt too.
            void rediscover().then(() => {
                for (const project of clients.values()) {
                    if (project.state === "ready") {
                        void refreshDependencyWatchers(project);
                    }
                }
            });
        }),
    );

    const restartServer = () => coordinator.request({ causes: ["command"] });

    context.subscriptions.push(
        vscode.workspace.onDidChangeConfiguration(async (e) => {
            if (!e.affectsConfiguration("aet-eaw-edit")) {
                return;
            }

            logLine(`Configuration changed. Servers running=${clients.size}`);

            const affects = (section: string) =>
                e.affectsConfiguration(section);
            const reason = restartReason(affects);

            // What travels once via initializationOptions, and the two executables picked when
            // the process starts, restart the servers - see settingsRestart.ts. With none running,
            // any change may be the one that lets one start. Every setting the validation checks
            // is among the restart settings, so any other change has nothing new to report.
            if (reason !== null || clients.size === 0) {
                await coordinator.request({
                    causes: reason === null ? ["start"] : [reason],
                    effects: previewEffectsChanged(affects),
                    assets: previewAssetsChanged(affects),
                });
            }
        }),
    );

    // Opening a .alo or .ala shows the model instead of the binary editor. Registered whatever the
    // feature flag says: the flag gates the SERVER endpoints, and a custom editor that vanishes with
    // a setting would leave those files opening as binary with no hint why.
    context.subscriptions.push(
        ModelPreviewEditorProvider.register(context, (uri) =>
            panelProject(ownerOf(uri.fsPath)?.spec.key),
        ),
    );

    // Every command, by area. Each module gets the same view of the host and nothing else of it.
    const deps: HostDeps = {
        clients,
        lsp,
        stores,
        localisationNavigatorProvider,
        storyNavigatorProvider,
        modBrowserProvider,
        effectiveObjectProvider,
        gatewayFor,
        panelProject,
        resolveProjectKey,
        commandProjectKey,
        locCommandProjectKey,
        ownerOf,
        folderProject,
        pickProject,
        reconcileClients,
        stopAllClients,
        restartServer,
        updateStatus,
    };
    whatsNewCommands.register(context, deps);
    luaDebugCommands.register(context, deps);
    localisationCommands.register(context, deps);
    modBrowserCommands.register(context, deps);
    storyCommands.register(context, deps);
    projectCommands.register(context, deps);
    diagnosticsCommands.register(context, deps);
    navigationCommands.register(context, deps);
    previewCommands.register(context, deps);
    gameObjectsCommands.register(context, deps);

    if (validateConfiguration(context.extensionPath)) {
        // The first discovery, in the same queue as everything after it.
        void rediscover();
    }
}

export async function deactivate(): Promise<void> {
    logLine("Extension deactivated.");
    statusItem?.hide();
    StoryGraphPanel.disposeAll();
    LocalisationEditorPanel.disposeAll();
    CreditsPreviewPanel.disposeAll();
    EncyclopediaPanel.disposeAll();
    ModBrowserPanel.disposeAll();
    ModelPreviewPanel.disposeAll();
    ModelInspectorPanel.disposeAll();
    WhatsNewPanel.disposeAll();
    await stopAllClients();
}
