// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import { ChildProcess, spawn } from "child_process";
import * as fs from "fs";
import * as path from "path";
import * as l10n from "@vscode/l10n";
import * as vscode from "vscode";
import {
    ClientCapabilities,
    FeatureState,
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
    StaticFeature,
    Trace,
} from "vscode-languageclient/node";
import { CreditsPreviewPanel } from "./creditsPreviewPanel";
import {
    effectiveObjectPath,
    effectiveObjectQuery,
    parseEffectiveObjectQuery,
} from "./effectiveObjectUri";
import { EncyclopediaPanel } from "./encyclopediaPanel";
import { ModBrowserPanel } from "./modBrowserPanel";
import {
    ModBrowserOutlineItem,
    ModBrowserViewProvider,
    OPEN_MOD_BROWSER,
} from "./modBrowserViewProvider";
import { PanelProject } from "./panelProject";
import { WebviewPanelHost } from "./webviewPanelHost";
import { ModelPreviewEditorProvider } from "./modelPreviewEditor";
import { ModelInspectorPanel } from "./modelInspectorPanel";
import { ModelPreviewPanel } from "./modelPreviewPanel";
import {
    type ModelPickItem,
    modelPickItems,
    pickedModelName,
} from "./modelPicker";
import { type ListModelsResult } from "./protocol/modelPreview";
import { type ServerStatus } from "./protocol/serverStatus";
import {
    type BugReportClientInfo,
    type BugReportOptions,
    flattenFlags,
    formatBugReport,
    formatExtendedStats,
} from "./bugReport";
import {
    type DiagnosticReportPayload,
    editorReport,
    gitHubIssue,
} from "./diagnosticReport";
import { offerShaderSources, shaderDirectory } from "./shaderSources";
import { initDialogGeometryStorage } from "./dialogGeometryStorage";
import { initPanelLayoutStorage } from "./panelLayoutStorage";
import { initViewerSettingsStorage } from "./viewerSettingsStorage";
import { initProjectSettingsStorage } from "./projectSettingsStorage";
import { LocalisationEditorPanel } from "./localisationEditorPanel";
import {
    LocalisationNavigatorViewProvider,
    LocTreeItem,
} from "./localisationNavigatorViewProvider";
import { logLine, setLogChannel } from "./log";
import { directoriesNeedingOwnWatcher } from "./dependencyWatchers";
import { LspGateway } from "./lsp/lspGateway";
import { RoutedLspGateway } from "./lsp/routedLspGateway";
import { stopClient } from "./lsp/stopClient";
import { discoverProjects } from "./projectDiscovery";
import {
    type ClientSpec,
    type ClientState,
    clientSpecs,
    diagnosticCollectionNameFor,
    documentSelectorFor,
    isInside,
    ownerKey,
    ownsPath,
    planChanges,
    statusText,
} from "./projectClientPlan";
import { WatcherPool } from "./watcherPool";
import {
    type NavigatorProject,
    ProjectGroupedTree,
} from "./projectGroupedTree";
import { vscodeMessageSink } from "./lsp/vscodeMessageSink";
import {
    LUA_DEBUG_TYPE,
    LuaDebugAdapterDescriptorFactory,
} from "./luaDebug/luaDebugAdapterFactory";
import { LuaDebugConfigurationProvider } from "./luaDebug/luaDebugConfigurationProvider";
import {
    LuaScriptsTreeItem,
    LuaScriptsViewProvider,
} from "./luaDebug/luaScriptsViewProvider";
import {
    resolveServerCommand,
    resolveServerEnvironment,
} from "./serverCommand";
import { serverProblem } from "./serverLaunch";
import {
    ConvertLocalisationFormatResult,
    ExportLocalisationToDatResult,
    GET_ENCYCLOPEDIA_ENTRY,
    GetEffectiveObjectResult,
    GetEncyclopediaEntryResult,
    GetLocalisationProjectsResult,
    GetRootLocalisationConfigResult,
    GetStoryPlotsResult,
    LOC_CATEGORY,
    SetLocalisationProjectFormatResult,
    StoryGraphChangedParams,
    StorySimChangedParams,
} from "./protocol";
import { formatChoices, formatOutcome } from "./localisationProjectFormat";
import { StoryGraphPanel } from "./storyGraphPanel";
import { graphTargets, type StoryGraphTarget } from "./storyGraphTarget";
import {
    StoryNavigatorViewProvider,
    StoryTreeItem,
} from "./storyNavigatorViewProvider";
import {
    ACCEPT,
    DECLINE,
    declinedMessage,
    diffTitle,
    outcomeOf,
    PgprojMigrationProposal,
    proposalMessage,
} from "./pgprojMigrationPrompt";

const CLIENT_ID = "aet.pg.swg.lsp";
const CLIENT_NAME = "Alamo Engine Tools - Empire at War Edit";
const REQUIRED_SERVER_VERSION = "0.4.1";

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

/** URI scheme for the read-only "effective object" virtual documents (variant inheritance). */
const EFFECTIVE_SCHEME = "aet-effective";

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
        const { objectId, projectKey } = parseEffectiveObjectQuery(uri.query);

        // The document IS the report here - it is read-only generated text, so a failure is written
        // into it as a comment rather than raised as a notification over the tab that just opened.
        const outcome = await gatewayFor(
            projectKey,
        ).request<GetEffectiveObjectResult>("aet/getEffectiveObject", {
            objectId,
        });

        if (!outcome.ok) {
            return outcome.reason === "offline"
                ? "<!-- EaWEdit: LSP server is not running. -->"
                : `<!-- EaWEdit: Failed to resolve effective object '${objectId}' - ${outcome.message} -->`;
        }
        if (!outcome.value.found) {
            return `<!-- EaWEdit: No object named '${objectId}' was found in the workspace. -->`;
        }
        return effectiveObjectBanner(objectId) + outcome.value.xml;
    }
}

/**
 * One language client and the server process behind it (#229).
 *
 * One runs per open project; a window with a single project runs exactly one, as it always did.
 */
interface ProjectClient {
    spec: ClientSpec;
    client: LanguageClient | undefined;
    /**
     * The server process, while one is running.
     *
     * Held because the language client does not hold it for us on the factory path, and because a
     * shutdown that times out leaves nothing else able to reach it. Cleared by the process's own
     * `exit`, so it never names something already gone.
     */
    process: ChildProcess | undefined;
    /** Requests to this project's server; offline once it failed or stopped. */
    gateway: LspGateway;
    state: ClientState;
    /** This server's watchers over directories outside the workspace folders. */
    dependencyWatchers: vscode.Disposable[];
    /** Disposed with the client: a project's own trace channel, when it has one. */
    owned: vscode.Disposable[];
}

/** The running clients, in discovery order. */
const clients = new Map<string, ProjectClient>();

/** The first project in discovery order. */
function primaryClient(): ProjectClient | undefined {
    return clients.values().next().value;
}

/** The workspaceState key of the project last picked for this window. */
const CHOSEN_PROJECT = "aet-eaw-edit.chosenProject";

/** Set on activation; the window's memory of the project last picked lives in its workspaceState. */
let windowState: vscode.Memento | undefined;

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
    const key = windowState?.get<string>(CHOSEN_PROJECT);
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
);

/** Gateways bound to one project each, by key. */
const projectGateways = new Map<string, RoutedLspGateway>();

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
        gateway: gatewayFor(resolved),
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
    await windowState?.update(CHOSEN_PROJECT, picked.key);
    return clients.get(picked.key);
}

/**
 * The gateway of the project a command acts on: the document in front's, else the one last picked
 * in this window, else the one picked now. Undefined when the picker was dismissed.
 */
async function commandGateway(): Promise<LspGateway | undefined> {
    const key = await commandProjectKey();
    return key === null ? undefined : gatewayFor(key);
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
 */
const WATCHED_GLOBS = [
    "**/*.xml",
    "**/*.lua",
    "**/*.pgproj",
    "**/*.csv",
    "**/*.properties",
    "**/*.dat",
];

/**
 * Watchers over project directories that lie OUTSIDE the open workspace folders.
 *
 * A bare glob only ever watches the folders the window is open on, so a project reached through
 * `projectReferences` was invisible: editing a dependency's XML, scripts, localisation or its own
 * `.pgproj` produced no `didChangeWatchedFiles` at all, and nothing re-indexed until the server was
 * restarted. These carry an absolute `RelativePattern` instead, and their events are forwarded by
 * hand because `synchronize.fileEvents` is fixed when the client is constructed, long before the
 * server has resolved which projects exist.
 */
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
    const answer = await project.gateway.request<{
        directories?: readonly string[];
    }>("aet/getWatchDirectories");
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

let traceChannel: vscode.LogOutputChannel | undefined;
let log: vscode.OutputChannel | undefined;

/**
 * vscode-languageclient v10 types `traceOutputChannel` as `LogOutputChannel` and writes protocol
 * trace via its `trace()` method - which a real `LogOutputChannel` hides unless its (user-controlled)
 * log level is set to Trace, and which prefixes every line with a level/timestamp. That breaks the
 * LSP trace view, where the `traceServer` setting (via `setTrace`) is meant to control visibility and
 * the JSON should appear verbatim. Adapt a plain `OutputChannel` to the `LogOutputChannel` shape so
 * every level writes through unchanged.
 */
function createTraceChannel(name: string): vscode.LogOutputChannel {
    const channel = vscode.window.createOutputChannel(name);
    const logLevelEmitter = new vscode.EventEmitter<vscode.LogLevel>();
    const write = (message: string, ..._args: unknown[]): void =>
        channel.appendLine(message);
    return Object.assign(channel, {
        logLevel: vscode.LogLevel.Trace,
        onDidChangeLogLevel: logLevelEmitter.event,
        trace: write,
        debug: write,
        info: write,
        warn: write,
        error: write,
    }) as unknown as vscode.LogOutputChannel;
}

function cfg(section: string) {
    return vscode.workspace.getConfiguration(`aet-eaw-edit.${section}`);
}

/**
 * A game install directory, from the setting that declares it or the one that used to.
 *
 * These were declared as `aet-eaw-edit.modVerify.*` and read as `aet-eaw-edit.lsp.source.*`, so
 * setting either did nothing: the server was never given a game path, and every asset that ships
 * with the game stayed unreachable. The declaration has moved to the key the reader asks for - the
 * one the model preview's own description already pointed at.
 *
 * The old key is still read so that anyone who set it gets the behaviour they were promised, rather
 * than a silent no-op turning into a silently ignored setting.
 */
function gameDirectory(
    name: "baseGameDirectory" | "expansionDirectory",
): string | undefined {
    return (
        cfg("lsp.source").get<string>(name) ||
        cfg("modVerify").get<string>(name) ||
        undefined
    );
}

/**
 * Builds the complete resolved feature-flag object sent to the server via initializationOptions.
 * The server's FeatureFlags record (Core\Configuration\FeatureFlags.cs) defaults everything to
 * true; the user-facing defaults, including every flag that is off out of the box, live in
 * package.json, so the fallbacks here must mirror package.json. Flags are restart-based: the config listener in activate() restarts the server when
 * any `aet-eaw-edit.features` value changes.
 */
function resolveFeatureFlags() {
    const features = cfg("features");
    const flag = (key: string, fallback: boolean) =>
        features.get<boolean>(key, fallback);
    return {
        xml: {
            completion: flag("xml.completion", true),
            hover: flag("xml.hover", true),
            diagnostics: flag("xml.diagnostics", true),
            goToDefinition: flag("xml.goToDefinition", true),
            findReferences: flag("xml.findReferences", true),
            rename: flag("xml.rename", true),
            codeLens: flag("xml.codeLens", true),
            inlayHints: flag("xml.inlayHints", true),
            codeActions: flag("xml.codeActions", true),
            linkedEditing: flag("xml.linkedEditing", true),
            autoCloseTag: flag("xml.autoCloseTag", true),
            strictness: flag("xml.strictness", true),
            formatting: flag("xml.formatting", true),
        },
        lua: {
            completion: flag("lua.completion", true),
            hover: flag("lua.hover", false),
            diagnostics: flag("lua.diagnostics", false),
            goToDefinition: flag("lua.goToDefinition", true),
            rename: flag("lua.rename", true),
            codeActions: flag("lua.codeActions", true),
            debugger: flag("lua.debugger", false),
            analyzer: flag("lua.analyzer", true),
        },
        tools: {
            localisation: flag("tools.localisation", false),
            storyEditor: flag("tools.storyEditor", false),
            storyEditing: flag("tools.storyEditing", false),
            storySimulator: flag("tools.storySimulator", false),
            variants: flag("tools.variants", true),
            encyclopedia: flag("tools.encyclopedia", true),
            modelPreview: flag("tools.modelPreview", true),
            modBrowser: flag("tools.modBrowser", true),
        },
        story: {
            discovery: flag("story.discovery", false),
            graphDiagnostics: flag("story.graphDiagnostics", false),
            symbols: flag("story.symbols", false),
            rename: flag("story.rename", false),
        },
        dialog: {
            diagnostics: flag("dialog.diagnostics", false),
            inlayHints: flag("dialog.inlayHints", false),
            goToDefinition: flag("dialog.goToDefinition", false),
            codeActions: flag("dialog.codeActions", false),
        },
    };
}

function validateConfiguration(extensionPath: string): boolean {
    if (!cfg("lsp").get<boolean>("enabled")) {
        return true;
    }

    let valid = true;

    const serverMissing = serverProblem(
        resolveServerCommand(extensionPath, []),
        fs.existsSync,
    );
    if (serverMissing) {
        void vscode.window.showErrorMessage(serverMissing);
        valid = false;
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
    // Where the server's debug log goes. Without it the server writes beside its own binary,
    // because a spawned process inherits the EDITOR's working directory - one session's log landed
    // inside the VS Code installation folder, and the workspace showed none at all. One project
    // logs in the first folder as before; several each log in their own directory.
    extraArgs.push(`--log-dir=${spec.logDirectory}`);
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

    const clientOptions: LanguageClientOptions = {
        synchronize: multi ? {} : { fileEvents },
        // Story-dialog scripts open as plaintext; the server gates all work on the pgproj
        // storyDialog registry scope, so other .txt files stay untouched.
        documentSelector: documentSelectorFor(spec),
        diagnosticCollectionName: diagnosticCollectionNameFor(CLIENT_ID, spec),
        traceOutputChannel: trace,
        initializationOptions: {
            workspaceRoot: spec.workspaceRoot,
            projectPath: spec.projectPath,
            baseGamePath: gameDirectory("baseGameDirectory"),
            // The user's own copy of the base shader SOURCES; never shipped with this extension.
            shaderPath: shaderDirectory(),
            expansionGamePath: gameDirectory("expansionDirectory"),
            locale: cfg("lsp").get<string>("locale", "en"),
            // The game's language, not this extension's. `locale` above sets the language the server
            // writes its own hover text and diagnostics in; this one picks the string table.
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
            // Which screen the encyclopedia card is drawn for. The game sizes that popup's glyphs
            // from the display it runs on, so a preview has to be told which display to match.
            encyclopedia: {
                screenWidth: cfg("encyclopedia").get<number>(
                    "screenWidth",
                    1920,
                ),
                screenHeight: cfg("encyclopedia").get<number>(
                    "screenHeight",
                    1080,
                ),
            },
            features: resolveFeatureFlags(),
        },
    };

    // The client's name is its output channel's: one per project when there are several.
    const lspClient = new LanguageClient(
        CLIENT_ID,
        multi ? `${CLIENT_NAME} (${spec.name})` : CLIENT_NAME,
        serverOptions,
        clientOptions,
    );
    project.client = lspClient;

    // Must be registered before start(): forces every language-feature capability to be advertised
    // statically, so registration is deterministic and immune to the dynamic-batch abort (and so
    // already-open documents get a didOpen). Otherwise a random subset of features may fail.
    lspClient.registerFeature(new ForceStaticCapabilitiesFeature());

    updateStatus();

    const traceLevel = cfg("lsp.debug").get<string>("traceServer", "off");
    const traceMap: Record<string, Trace> = {
        off: Trace.Off,
        messages: Trace.Messages,
        verbose: Trace.Verbose,
    };
    const resolvedTrace = traceMap[traceLevel] ?? Trace.Off;

    lspClient
        .start()
        .then(async () => {
            // setTrace must be called after start() resolves: before that, activeConnection()
            // returns undefined and the trace level is stored but never applied to the transport.
            await lspClient.setTrace(resolvedTrace);
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

            // vscode-languageclient's hookConfigurationChanged reads aet.pg.swg.lsp.trace.server
            // (the CLIENT_ID namespace), which is not in settings, so it resets trace to Off on
            // every VS Code config change. Register our own listener - after hookConfigurationChanged's
            // listener, so it fires last and wins - to re-apply the user's actual setting.
            context.subscriptions.push(
                vscode.workspace.onDidChangeConfiguration(() => {
                    const level = cfg("lsp.debug").get<string>(
                        "traceServer",
                        "off",
                    );
                    if (project.client === lspClient) {
                        void lspClient.setTrace(traceMap[level] ?? Trace.Off);
                    }
                }),
            );
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
                ACCEPT,
                DECLINE,
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

    lspClient.onNotification("$/workspaceScanComplete", () => {
        logLine(`${label}Workspace scan complete.`);
        project.state = "ready";
        updateStatus();
        // Only now does the server know which projects the workspace resolved to, so this is the
        // first moment the dependency directories can be asked for. The notification fires on
        // startup only, which is why a .pgproj change rebuilds them separately.
        void refreshDependencyWatchers(project);
        // Both navigators fetch now rather than when they are first opened. A tree view only asks
        // for its children on reveal, so without this the first click on either paid for a round
        // trip - and until the scan finished they had nothing to show but a "loading" line.
        void storyNavigatorProvider?.preload(viewKey(project));
        void localisationNavigatorProvider?.preload(viewKey(project));
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
    lsp.routesChanged();
    WebviewPanelHost.setProjectCount(clients.size);
    await Promise.all(running.map((project) => stopProjectClient(project)));
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
        running.map((p) => ({ name: p.spec.name, state: p.state })),
    );
    if (running.length === 1) {
        const state = running[0].state;
        if (state === "indexing") {
            statusItem.tooltip = vscode.l10n.t(
                "The server is connected. Language features become complete when the scan finishes.",
            );
        } else if (state === "ready") {
            statusItem.tooltip = undefined;
        }
    } else {
        statusItem.tooltip = running
            .map((p) => `${p.spec.name} - ${p.state}`)
            .join("\n");
    }
    statusItem.show();
}

/** Serialises discovery runs: a folder change and a project file event can arrive together. */
let reconciling: Promise<void> = Promise.resolve();

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
    const specs = clientSpecs(discovery, folders, onDiskSpelling);

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
    lsp.routesChanged();
    WebviewPanelHost.setProjectCount(clients.size);
    storyNavigatorProvider?.projectsChanged();
    localisationNavigatorProvider?.projectsChanged();
    modBrowserProvider?.projectsChanged();
    updateStatus();
}

/** A directory as the disk spells it, so a document selector matches VS Code's spelling. */
function onDiskSpelling(directory: string): string {
    try {
        return fs.realpathSync.native(directory).replace(/\\/g, "/");
    } catch {
        return directory;
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
                onProjectFileTouched(context);
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

function onProjectFileTouched(context: vscode.ExtensionContext): void {
    // Not validated here: the configuration was checked when the servers started, and a project
    // file saved on every keystroke must not raise the same settings errors each time.
    if (cfg("lsp").get<boolean>("enabled") !== true) {
        return;
    }
    void reconcileClients(context).then(() => {
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

/**
 * The basic bug report block: what the client knows about itself, plus the server's own status
 * when it is running. Outcomes only - no path and no project name - because it goes into public
 * issues.
 */
async function collectBugReport(
    context: vscode.ExtensionContext,
    gateway: LspGateway,
    options: BugReportOptions = {},
): Promise<string> {
    const { client, server } = await reportInputs(
        context,
        gateway,
        options.extended === true,
    );
    return formatBugReport(client, server, options);
}

/** What the client knows about itself, and the server's status when it is running. */
async function reportInputs(
    context: vscode.ExtensionContext,
    gateway: LspGateway,
    extended: boolean,
): Promise<{ client: BugReportClientInfo; server: ServerStatus | null }> {
    const client: BugReportClientInfo = {
        extensionVersion: String(
            context.extension.packageJSON.version ?? "unknown",
        ),
        vscodeVersion: vscode.version,
        os: `${process.platform} ${process.arch}`,
        workspaceFolders: vscode.workspace.workspaceFolders?.length ?? 0,
        workspaceFile: vscode.workspace.workspaceFile !== undefined,
        featureFlags: flattenFlags(resolveFeatureFlags()),
    };

    const status = gateway.isRunning
        ? await gateway.request<ServerStatus>("aet/getServerStatus", {
              extended,
          })
        : undefined;

    return { client, server: status?.ok ? status.value : null };
}

/**
 * Basic or basic plus extended stats - asked every time rather than remembered, because the
 * extended counts describe the project's shape and sharing them is a choice made per report.
 * Undefined when dismissed.
 */
async function pickReportTier(
    title: string,
): Promise<{ extended: boolean } | undefined> {
    return vscode.window.showQuickPick(
        [
            {
                label: vscode.l10n.t("Basic info"),
                description: vscode.l10n.t(
                    "Versions, sources and workspace checks",
                ),
                extended: false,
            },
            {
                label: vscode.l10n.t("Basic + extended stats"),
                description: vscode.l10n.t(
                    "Adds dependency, index and asset counts",
                ),
                extended: true,
            },
        ],
        { title, placeHolder: vscode.l10n.t("What to include") },
    );
}

/**
 * Opens a prefilled GitHub issue for one diagnostic. The author reviews and submits it in the
 * browser; nothing is posted from here, which is why no login is needed.
 */
async function reportDiagnosticOnGitHub(
    context: vscode.ExtensionContext,
    gateway: LspGateway,
    report: DiagnosticReportPayload,
): Promise<void> {
    const tier = await pickReportTier(`Report ${report.id} on GitHub`);
    if (!tier) {
        return;
    }

    const { client, server } = await reportInputs(
        context,
        gateway,
        tier.extended,
    );
    // The per-type symbol list stays out: it runs to dozens of lines and the prefill is capped.
    const extended =
        tier.extended && server?.extended
            ? formatExtendedStats(server.extended, false)
            : null;
    const issue = gitHubIssue(
        report,
        formatBugReport(client, server),
        extended,
    );

    if (!issue.fits) {
        const choice = await vscode.window.showWarningMessage(
            vscode.l10n.t(
                "EaWEdit: The report for {0} is too long to prefill an issue",
                report.id,
            ),
            vscode.l10n.t("Open report in editor"),
        );
        if (choice === vscode.l10n.t("Open report in editor")) {
            await openDiagnosticReport(context, gateway, report, tier);
        }
        return;
    }

    await vscode.env.openExternal(vscode.Uri.parse(issue.url));

    if (issue.trimmed.length > 0) {
        const left = issue.trimmed.map((t) =>
            t === "extended"
                ? vscode.l10n.t("extended stats")
                : vscode.l10n.t("part of the basic info"),
        );
        const choice = await vscode.window.showInformationMessage(
            vscode.l10n.t(
                "EaWEdit: Issue prefill shortened - {0} left out",
                left.join(vscode.l10n.t(" and ")),
            ),
            vscode.l10n.t("Open full report"),
        );
        if (choice === vscode.l10n.t("Open full report")) {
            await openDiagnosticReport(context, gateway, report, tier);
        }
    }
}

/** The same report, untrimmed, in an untitled Markdown tab - for pasting anywhere. */
async function openDiagnosticReport(
    context: vscode.ExtensionContext,
    gateway: LspGateway,
    report: DiagnosticReportPayload,
    chosen?: { extended: boolean },
): Promise<void> {
    const tier =
        chosen ?? (await pickReportTier(`Open report for ${report.id}`));
    if (!tier) {
        return;
    }

    const { client, server } = await reportInputs(
        context,
        gateway,
        tier.extended,
    );
    const full = formatBugReport(client, server, {
        extended: tier.extended,
        symbolTypes: tier.extended,
    });
    const document = await vscode.workspace.openTextDocument({
        language: "markdown",
        content: editorReport(report, full),
    });
    await vscode.window.showTextDocument(document);
}

/**
 * Asks which model to preview, from every model the project can reach - its own, its dependencies'
 * and the base game's - so a model that is not in the explorer can be found without its name.
 *
 * A QuickPick that also accepts what was typed: the list is the asset catalog, and a model saved a
 * moment ago may not be in it yet.
 */
async function pickModel(gateway: LspGateway): Promise<string | null> {
    const result = await gateway.requestOrReport<ListModelsResult>(
        "aet/listModels",
        {},
        "Could not list models",
    );
    if (!result) {
        return null;
    }

    const pick = vscode.window.createQuickPick<ModelPickItem>();
    pick.title = vscode.l10n.t("Preview Model");
    pick.placeholder = "Model file name, e.g. EV_StarDestroyer.ALO";
    pick.matchOnDescription = true;
    pick.items = modelPickItems(result.models);

    const name = await new Promise<string | null>((resolve) => {
        pick.onDidAccept(() => {
            resolve(pickedModelName(pick.activeItems[0], pick.value));
            pick.hide();
        });
        pick.onDidHide(() => resolve(null));
        pick.show();
    });

    pick.dispose();
    return name;
}

export async function activate(
    context: vscode.ExtensionContext,
): Promise<void> {
    // The modules that cannot import `vscode` translate through `@vscode/l10n`; they read the same
    // bundle VS Code loaded for the display language. English ships no bundle: the texts are the keys.
    if (vscode.l10n.bundle !== undefined) {
        l10n.config({ contents: vscode.l10n.bundle });
    }

    // Per project, not per machine: where a dialog belongs depends on the mod being edited.
    initDialogGeometryStorage(context.workspaceState);

    // The preview's room settings follow the person rather than the project, so globalState.
    // The preview's PROJECT settings do not - the weapon bench, the faction and its colour all name
    // things out of the mod's own tree - so they go to workspaceState beside the dialog geometry.
    initViewerSettingsStorage(context.globalState);
    initProjectSettingsStorage(context.workspaceState);

    // Same reasoning: how wide you like a dock is a property of your screen and your habits,
    // not of the mod you have open.
    initPanelLayoutStorage(context.globalState);

    // The Lua debugger. Both registrations stay in place whatever the feature flag says: the
    // debug type must exist for launch.json to validate, and a disabled feature answers with the
    // setting to turn on rather than by vanishing.
    context.subscriptions.push(
        vscode.debug.registerDebugConfigurationProvider(
            LUA_DEBUG_TYPE,
            new LuaDebugConfigurationProvider((folder) => {
                const project =
                    folder === undefined ? undefined : folderProject(folder);
                return project === undefined
                    ? lsp
                    : gatewayFor(project.spec.key);
            }),
        ),
        vscode.debug.registerDebugAdapterDescriptorFactory(
            LUA_DEBUG_TYPE,
            new LuaDebugAdapterDescriptorFactory(
                context.extensionPath,
                logLine,
            ),
        ),
    );

    // The scripts view follows whichever eaw-lua session is active; with none it says so in place.
    const luaScripts = new LuaScriptsViewProvider();
    context.subscriptions.push(
        luaScripts,
        vscode.commands.registerCommand(
            "aet-eaw-edit.luaDebug.refreshScripts",
            () => luaScripts.refresh(),
        ),
        vscode.commands.registerCommand(
            "aet-eaw-edit.luaDebug.refreshThreads",
            (item?: LuaScriptsTreeItem) => luaScripts.refreshThreads(item),
        ),
        vscode.commands.registerCommand(
            "aet-eaw-edit.luaDebug.breakScript",
            (item?: LuaScriptsTreeItem) => luaScripts.breakScript(item),
        ),
        vscode.commands.registerCommand(
            "aet-eaw-edit.luaDebug.breakThread",
            (item?: LuaScriptsTreeItem) => luaScripts.breakThread(item),
        ),
    );

    localisationNavigatorProvider = new ProjectGroupedTree<LocTreeItem>(
        navigatorProjects,
        (key) => new LocalisationNavigatorViewProvider(gatewayFor(key)),
    );
    context.subscriptions.push(
        vscode.window.registerTreeDataProvider(
            LocalisationNavigatorViewProvider.viewId,
            localisationNavigatorProvider,
        ),
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.refreshLocalisationNavigator",
            () => localisationNavigatorProvider?.refresh(),
        ),
        // Hosted here rather than in the tree so the palette can reach it too. Lifted out of the
        // sidebar webview, which owned this quick pick before the tree existed.
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.newLocalisationProject",
            async () => {
                const choice = await vscode.window.showQuickPick(
                    [
                        {
                            label: vscode.l10n.t(
                                "Initialise from the game baseline",
                            ),
                            command: "aet-eaw-edit.lsp.initLocalisationProject",
                        },
                        {
                            label: vscode.l10n.t(
                                "Import existing localisation files",
                            ),
                            command:
                                "aet-eaw-edit.lsp.importLocalisationProject",
                        },
                    ],
                    {
                        placeHolder: vscode.l10n.t(
                            "Create a localisation project",
                        ),
                    },
                );
                if (choice) {
                    await vscode.commands.executeCommand(choice.command);
                }
            },
        ),
        // Opens the localisation grid in its own tab. Invoked with the file tree item, or without
        // arguments from the command palette (quick-picks a file, grouped by category).
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.openLocalisationEditor",
            async (arg?: LocTreeItem) => {
                // A clicked row belongs to the project whose tree it is in.
                const projectKey =
                    (arg && localisationNavigatorProvider?.projectOf(arg)) ??
                    (await commandProjectKey());
                if (projectKey === null) {
                    return;
                }
                const lsp = gatewayFor(projectKey);
                if (!lsp.requireRunning()) {
                    return;
                }

                // The quick pick below is for the palette, where nothing was clicked. A node that
                // WAS clicked and carries no project is a node that should not have offered this
                // action - answering it by listing every localisation file in the workspace is how
                // clicking a credits group ended up offering unrelated .dat translation files.
                if (arg !== undefined && arg.project === undefined) {
                    vscode.window.showWarningMessage(
                        vscode.l10n.t(
                            "EaWEdit: That node groups several files rather than being one. Open one of the files under it.",
                        ),
                    );
                    return;
                }

                let project = arg?.project;
                if (!project) {
                    const result =
                        await lsp.requestOrReport<GetLocalisationProjectsResult>(
                            "aet/getLocalisationProjects",
                            {},
                            "could not list localisation files",
                        );
                    if (!result) {
                        return;
                    }
                    if (result.error || !result.projects?.length) {
                        vscode.window.showWarningMessage(
                            vscode.l10n.t(
                                "EaWEdit: {0}",
                                result.error ??
                                    vscode.l10n.t(
                                        "no localisation files found in this workspace.",
                                    ),
                            ),
                        );
                        return;
                    }

                    const picked = await vscode.window.showQuickPick(
                        result.projects
                            .slice()
                            .sort(
                                (a, b) =>
                                    a.category.localeCompare(b.category) ||
                                    a.label.localeCompare(b.label),
                            )
                            .map((p) => ({
                                label: p.label,
                                description:
                                    p.category === LOC_CATEGORY.credits
                                        ? vscode.l10n.t("Credits")
                                        : vscode.l10n.t("Text"),
                                detail: p.filePath,
                                project: p,
                            })),
                        {
                            placeHolder: vscode.l10n.t(
                                "Open a localisation file",
                            ),
                        },
                    );
                    if (!picked) {
                        return;
                    }
                    project = picked.project;
                }

                // A node standing for a set of single-language files opens them as one table, so a
                // translation sits beside its source. A language inside that set opens the same
                // table with the other columns hidden - the set is the project either way, which is
                // why both are named after the set rather than after whichever file was clicked.
                LocalisationEditorPanel.showSet(
                    arg?.setFilePaths ?? [project.filePath],
                    arg?.setLabel ?? project.label,
                    project.category,
                    context.extensionUri,
                    panelProject(projectKey),
                    arg?.focusLanguage,
                );
            },
        ),
        // The crawl lives in the editor title bar, where a preview belongs - the same place a
        // Markdown or LaTeX editor puts one. Both are gated to the credits panel by its view type.
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.previewCreditsCrawl",
            () => LocalisationEditorPanel.previewCrawl(false),
        ),
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.previewCreditsCrawlFullScreen",
            () => LocalisationEditorPanel.previewCrawl(true),
        ),
        // Export was only ever reachable from inside the old sidebar webview; the tree is the
        // natural home for it, and a menu entry needs a real command behind it.
        // Opening the raw file is a thing you do to a file, so it belongs on the file's own menu in
        // the tree rather than inside the editor showing it.
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.openLocalisationAsText",
            async (arg?: LocTreeItem) => {
                const filePath = arg?.project?.filePath;
                if (!filePath) {
                    vscode.window.showWarningMessage(
                        vscode.l10n.t(
                            "EaWEdit: No localisation file selected.",
                        ),
                    );
                    return;
                }
                await vscode.window.showTextDocument(
                    vscode.Uri.file(filePath),
                    { viewColumn: vscode.ViewColumn.Beside },
                );
            },
        ),
        // Convert reaches here from two places - the editor's dock and the palette - so the request
        // and the way its outcome is reported live in one place rather than two that drift.
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.convertLocalisationFormat",
            async (
                arg?:
                    | LocTreeItem
                    | {
                          filePath?: string;
                          targetFormat?: string;
                          category?: string;
                          /** The asking panel's project (#276). */
                          projectKey?: string;
                      },
            ) => {
                const projectKey = await locCommandProjectKey(arg);
                if (projectKey === null) {
                    return;
                }
                const lsp = gatewayFor(projectKey);
                if (!lsp.requireRunning()) {
                    return;
                }

                const direct = arg as
                    | {
                          filePath?: string;
                          targetFormat?: string;
                          category?: string;
                      }
                    | undefined;
                let filePath =
                    direct?.filePath ??
                    (arg as LocTreeItem | undefined)?.project?.filePath;
                let category =
                    direct?.category ??
                    (arg as LocTreeItem | undefined)?.project?.category;

                if (!filePath) {
                    const result =
                        await lsp.requestOrReport<GetLocalisationProjectsResult>(
                            "aet/getLocalisationProjects",
                            {},
                            "could not list localisation files",
                        );
                    if (!result) {
                        return;
                    }
                    if (result.error || !result.projects?.length) {
                        vscode.window.showWarningMessage(
                            vscode.l10n.t(
                                "EaWEdit: {0}",
                                result.error ??
                                    vscode.l10n.t(
                                        "no localisation files found in this workspace.",
                                    ),
                            ),
                        );
                        return;
                    }
                    const picked = await vscode.window.showQuickPick(
                        result.projects.map((p) => ({
                            label: p.label,
                            description: p.resourceType.toUpperCase(),
                            detail: p.filePath,
                            project: p,
                        })),
                        {
                            placeHolder: vscode.l10n.t(
                                "Convert which localisation file?",
                            ),
                        },
                    );
                    if (!picked) {
                        return;
                    }
                    filePath = picked.project.filePath;
                    category = picked.project.category;
                }

                let targetFormat = direct?.targetFormat;
                if (!targetFormat) {
                    // DAT is absent on purpose: it is the game's load format, produced by Export to DAT.
                    const picked = await vscode.window.showQuickPick(
                        [
                            {
                                label: vscode.l10n.t("CSV"),
                                description: vscode.l10n.t(
                                    "Comma-separated values (.csv)",
                                ),
                            },
                            {
                                label: vscode.l10n.t("XML"),
                                description: vscode.l10n.t(
                                    "eaw-translation v1 XML (.xml)",
                                ),
                            },
                            {
                                label: vscode.l10n.t("NLS"),
                                description: vscode.l10n.t(
                                    "Java-style properties (.properties)",
                                ),
                            },
                        ],
                        {
                            title: vscode.l10n.t("Convert Localisation File"),
                            placeHolder: vscode.l10n.t(
                                "Write it in which format?",
                            ),
                        },
                    );
                    if (!picked) {
                        return;
                    }
                    targetFormat = picked.label;
                }

                const result =
                    await lsp.requestOrReport<ConvertLocalisationFormatResult>(
                        "aet/convertLocalisationFormat",
                        { projectFilePath: filePath, targetFormat },
                        `could not convert ${path.basename(filePath)} to ${targetFormat}`,
                    );
                if (!result) {
                    return;
                }

                if (result.error) {
                    vscode.window.showErrorMessage(
                        vscode.l10n.t("EaWEdit: {0}", result.error),
                    );
                    return;
                }

                // A repoint leaves everything still in the old format unloaded. Worth saying now,
                // while it is one line of the .pgproj away from being put back.
                const orphans =
                    result.projectFormatChanged &&
                    result.otherFilesInOldFormat > 0
                        ? ` ${result.otherFilesInOldFormat} other file(s) are still in the previous format` +
                          " and are no longer loaded."
                        : "";
                // A single-language target (NLS) writes one file per language rather than dropping
                // all but one, so the message has to be able to report more than a single path.
                const written =
                    result.writtenPaths ??
                    (result.writtenPath ? [result.writtenPath] : []);
                const wrote =
                    written.length > 1
                        ? `wrote ${written.length} files, one per language: ${written.map((p) => path.basename(p)).join(", ")}`
                        : `wrote ${written[0]}`;
                const choice = await vscode.window.showInformationMessage(
                    vscode.l10n.t(
                        "EaWEdit: {0}. The original file was kept.{1}",
                        wrote,
                        orphans,
                    ),
                    vscode.l10n.t("Open"),
                );
                localisationNavigatorProvider?.refresh();
                if (choice === vscode.l10n.t("Open") && result.writtenPath) {
                    LocalisationEditorPanel.show(
                        result.writtenPath,
                        path.basename(result.writtenPath),
                        category ?? LOC_CATEGORY.text,
                        context.extensionUri,
                        panelProject(projectKey),
                    );
                }
            },
        ),
        // Which format the project LOADS (#121), as opposed to converting a file. The palette entry and
        // the navigator's title-bar button are this one command. Nothing is converted: the .pgproj is
        // repointed and the project reloads, so the message has to say how many files that leaves.
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.setLocalisationProjectFormat",
            async () => {
                const projectKey = await commandProjectKey();
                if (projectKey === null) {
                    return;
                }
                const lsp = gatewayFor(projectKey);
                if (!lsp.requireRunning()) {
                    return;
                }

                const config =
                    await lsp.request<GetRootLocalisationConfigResult>(
                        "aet/getRootLocalisationConfig",
                    );
                const current =
                    config.ok && config.value.configured
                        ? config.value.type
                        : null;

                const picked = await vscode.window.showQuickPick(
                    formatChoices(current),
                    {
                        title: vscode.l10n.t("Set Localisation Project Format"),
                        placeHolder: vscode.l10n.t(
                            "Load the project's text from which format? No files are converted",
                        ),
                    },
                );
                if (!picked) {
                    return;
                }

                const result =
                    await lsp.requestOrReport<SetLocalisationProjectFormatResult>(
                        "aet/setLocalisationProjectFormat",
                        { format: picked.format },
                        `could not set the localisation format to ${picked.format}`,
                    );
                if (!result) {
                    return;
                }

                const outcome = formatOutcome(result);
                if (outcome.kind === "error") {
                    vscode.window.showErrorMessage(outcome.text);
                    return;
                }
                if (outcome.kind === "warning") {
                    vscode.window.showWarningMessage(outcome.text);
                } else {
                    vscode.window.showInformationMessage(outcome.text);
                }
                if (result.changed) {
                    localisationNavigatorProvider?.refresh();
                }
            },
        ),
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.exportLocalisationToDat",
            async (arg?: LocTreeItem & { projectKey?: string }) => {
                const filePath = arg?.project?.filePath;
                if (!filePath) {
                    vscode.window.showWarningMessage(
                        vscode.l10n.t(
                            "EaWEdit: No localisation file selected.",
                        ),
                    );
                    return;
                }
                const projectKey = await locCommandProjectKey(arg);
                if (projectKey === null) {
                    return;
                }
                const lsp = gatewayFor(projectKey);
                if (!lsp.requireRunning()) {
                    return;
                }

                const result =
                    await lsp.requestOrReport<ExportLocalisationToDatResult>(
                        "aet/exportLocalisationToDat",
                        { projectFilePath: filePath },
                        "DAT export failed",
                    );
                if (!result) {
                    return;
                }

                if (result.error) {
                    vscode.window.showErrorMessage(
                        vscode.l10n.t(
                            "EaWEdit: DAT export failed - {0}",
                            result.error,
                        ),
                    );
                    return;
                }
                vscode.window.showInformationMessage(
                    result.writtenFiles.length === 1
                        ? vscode.l10n.t("EaWEdit: Exported 1 DAT file.")
                        : vscode.l10n.t(
                              "EaWEdit: Exported {0} DAT files.",
                              result.writtenFiles.length,
                          ),
                );
            },
        ),
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
        () => new ModBrowserViewProvider(),
    );
    context.subscriptions.push(
        vscode.window.registerTreeDataProvider(
            ModBrowserViewProvider.viewId,
            modBrowserProvider,
        ),
        // From an outline row, or from the palette, which opens the game data.
        vscode.commands.registerCommand(
            OPEN_MOD_BROWSER,
            async (arg?: ModBrowserOutlineItem) => {
                const projectKey =
                    (arg && modBrowserProvider?.projectOf(arg)) ??
                    (await commandProjectKey());
                if (projectKey === null) {
                    return;
                }
                const project = panelProject(projectKey);
                if (!project.gateway.requireRunning()) {
                    return;
                }
                ModBrowserPanel.show(
                    context.extensionUri,
                    project,
                    arg?.outline ?? "gameData",
                );
            },
        ),
    );

    storyNavigatorProvider = new ProjectGroupedTree<StoryTreeItem>(
        navigatorProjects,
        (key) => new StoryNavigatorViewProvider(gatewayFor(key)),
    );
    context.subscriptions.push(
        vscode.window.registerTreeDataProvider(
            StoryNavigatorViewProvider.viewId,
            storyNavigatorProvider,
        ),
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.refreshStoryNavigator",
            () => storyNavigatorProvider?.refresh(),
        ),
        // Opens the read-only story graph panel. Invoked with the campaign tree item (inline icon
        // in the navigator) or without arguments from the command palette (quick-picks a campaign).
        // A graph is one campaign FACTION, so the tree offers this on its faction node and hands
        // both halves over. The palette has no node to hand over, so it asks - once, over a flat
        // list of every pair, rather than a campaign prompt followed by a faction prompt.
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.openStoryGraph",
            async (arg?: StoryTreeItem) => {
                // A clicked row belongs to the project whose tree it is in.
                const projectKey =
                    (arg && storyNavigatorProvider?.projectOf(arg)) ??
                    (await commandProjectKey());
                if (projectKey === null) {
                    return;
                }
                const lsp = gatewayFor(projectKey);
                if (!lsp.requireRunning()) {
                    return;
                }

                // A battle node carries its key in fileName and its name in the label; it opens the
                // battle's own graph, the sub-graph the galactic one shows as a portal.
                let target: StoryGraphTarget | undefined =
                    arg?.campaignName && arg?.factionName
                        ? {
                              campaign: arg.campaignName,
                              faction: arg.factionName,
                              ...(arg.kind === "battle" && arg.fileName
                                  ? {
                                        scope: arg.fileName,
                                        scopeLabel: String(
                                            arg.label ?? arg.fileName,
                                        ),
                                    }
                                  : {}),
                          }
                        : undefined;

                if (!target) {
                    const result =
                        await lsp.requestOrReport<GetStoryPlotsResult>(
                            "aet/getStoryPlots",
                            {},
                            "cannot load story campaigns",
                        );
                    if (!result) {
                        return;
                    }
                    if (result.error) {
                        vscode.window.showWarningMessage(
                            vscode.l10n.t("EaWEdit: {0}", result.error),
                        );
                        return;
                    }

                    const items = graphTargets(result.campaigns ?? []);
                    if (!items.length) {
                        vscode.window.showInformationMessage(
                            vscode.l10n.t(
                                "EaWEdit: No story campaigns found in this workspace.",
                            ),
                        );
                        return;
                    }
                    const picked = await vscode.window.showQuickPick(items, {
                        title: vscode.l10n.t("Open Story Graph"),
                        placeHolder: vscode.l10n.t(
                            "Select a campaign faction or one of its battles",
                        ),
                    });
                    target = picked?.target;
                }
                if (target) {
                    StoryGraphPanel.show(
                        target,
                        context.extensionUri,
                        panelProject(projectKey),
                        undefined,
                        false,
                    );
                }
            },
        ),
        // The server's resolution is the only one: the model resolved every manifest entry to its
        // document when it was assembled, case-insensitively as the engine reads files. There is
        // no name search here - a workspace glob is case-sensitive and would answer by a different
        // rule than the one the model used, which is how a file that was right there read as missing.
        // Invoked from a navigator node's inline button with the node; a node with nothing to open
        // offers the disabled twin instead, so this never runs for one.
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.openStoryFile",
            async (item?: StoryTreeItem) => {
                if (!item?.openUri) {
                    return;
                }
                try {
                    const doc = await vscode.workspace.openTextDocument(
                        vscode.Uri.parse(item.openUri),
                    );
                    const line = item.openLine ?? 0;
                    await vscode.window.showTextDocument(doc, {
                        preview: true,
                        selection: new vscode.Range(line, 0, line, 0),
                    });
                } catch (e) {
                    vscode.window.showWarningMessage(
                        vscode.l10n.t(
                            "EaWEdit: Cannot open '{0}' - {1}",
                            String(item.label),
                            String(e),
                        ),
                    );
                }
            },
        ),
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.openStoryFileUnavailable",
            () => undefined,
        ),
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

    log = vscode.window.createOutputChannel("EaWEdit");
    setLogChannel(log);
    context.subscriptions.push(log);

    traceChannel = createTraceChannel("EaWEdit LSP Trace");
    context.subscriptions.push(traceChannel);

    logLine("Extension activated.");
    logLine(`  lsp.enabled      = ${cfg("lsp").get<boolean>("enabled")}`);
    logLine(
        `  lsp.devMode      = ${cfg("lsp.devMode").get<boolean>("enabled", false)}`,
    );
    logLine(
        `  lsp.executable   = ${cfg("lsp").get<string>("executable") || "(bundled)"}`,
    );
    logLine(
        `  lsp.projectPath  = ${cfg("lsp.devMode").get<string>("projectPath") ?? "(not set)"}`,
    );

    windowState = context.workspaceState;
    watchWorkspace(context);
    // Changes the project remembered for commands; the window asks on its own only when the
    // document in front belongs to no project and nothing was picked yet.
    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.selectProject",
            async () => {
                if (clients.size <= 1) {
                    void vscode.window.showInformationMessage(
                        vscode.l10n.t("EaWEdit: One project open"),
                    );
                    return;
                }
                await pickProject();
            },
        ),
    );
    // A folder added or removed changes which projects the window holds; only the servers of the
    // projects that came or went start or stop.
    context.subscriptions.push(
        vscode.workspace.onDidChangeWorkspaceFolders(() => {
            if (cfg("lsp").get<boolean>("enabled") === true) {
                // A server kept running may now find a dependency inside a folder, or outside one:
                // its own watchers over directories outside the folders are rebuilt too.
                void reconcileClients(context).then(() => {
                    for (const project of clients.values()) {
                        if (project.state === "ready") {
                            void refreshDependencyWatchers(project);
                        }
                    }
                });
            }
        }),
    );

    const restartServer = async () => {
        await stopAllClients();
        if (
            cfg("lsp").get<boolean>("enabled") === true &&
            validateConfiguration(context.extensionPath)
        ) {
            await reconcileClients(context);
        }
    };

    context.subscriptions.push(
        vscode.workspace.onDidChangeConfiguration(async (e) => {
            if (!e.affectsConfiguration("aet-eaw-edit")) {
                return;
            }

            const enabled = cfg("lsp").get<boolean>("enabled") === true;
            logLine(
                `Configuration changed. lsp.enabled=${enabled}, servers running=${clients.size}`,
            );

            if (!enabled && clients.size > 0) {
                await stopAllClients();
            } else if (
                enabled &&
                clients.size === 0 &&
                validateConfiguration(context.extensionPath)
            ) {
                logLine("Starting LSP server after configuration change.");
                await reconcileClients(context);
            } else if (
                clients.size > 0 &&
                (e.affectsConfiguration("aet-eaw-edit.features") ||
                    e.affectsConfiguration("aet-eaw-edit.lsp.executable") ||
                    e.affectsConfiguration("aet-eaw-edit.lua.analyzerPath"))
            ) {
                // Feature flags travel once via initializationOptions, and the two executables are
                // picked when the process starts - restart to apply any of them.
                logLine(
                    "Feature flags or executables changed - restarting LSP server.",
                );
                await restartServer();
            } else {
                validateConfiguration(context.extensionPath);
            }
        }),
    );

    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.debug.forceStartup",
            async () => {
                await stopAllClients();
                const serverMissing = serverProblem(
                    resolveServerCommand(context.extensionPath, []),
                    fs.existsSync,
                );
                if (serverMissing) {
                    void vscode.window.showErrorMessage(serverMissing);
                } else {
                    await reconcileClients(context);
                }
            },
        ),
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.restartServer",
            restartServer,
        ),
    );

    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.revalidateWorkspace",
            async () => {
                // The workspace is the window: every project's server revalidates its own files.
                // Asking one project left the others' diagnostics as they were (#167).
                const keys = clients.size > 0 ? [...clients.keys()] : [""];
                await Promise.all(
                    keys.map((key) =>
                        gatewayFor(key).executeCommand(
                            "aet-eaw-edit.lsp.revalidateWorkspace",
                            [],
                            "could not revalidate the workspace",
                        ),
                    ),
                );
            },
        ),
    );

    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.newModProject",
            async () => {
                // Writes a new project wherever it is told to; no open project is asked about it.
                if (!lsp.requireRunning()) {
                    return;
                }
                const name = await vscode.window.showInputBox({
                    prompt: vscode.l10n.t("Mod name"),
                    placeHolder: vscode.l10n.t("My Awesome Mod"),
                    validateInput: (v) =>
                        v?.trim() ? null : "Name is required",
                });
                if (!name) {
                    return;
                }
                const folders = await vscode.window.showOpenDialog({
                    canSelectFolders: true,
                    canSelectFiles: false,
                    canSelectMany: false,
                    openLabel: "Select mod root folder",
                });
                if (!folders?.length) {
                    return;
                }
                // Announced only once it actually ran. The old code said "created" whether or not the
                // request got through, so a server that had died reported a project that did not exist.
                const created = await lsp.executeCommand(
                    "aet-eaw-edit.lsp.newModProject",
                    [{ name: name.trim(), path: folders[0].fsPath }],
                    `could not create the mod project '${name.trim()}'`,
                );
                if (created) {
                    vscode.window.showInformationMessage(
                        vscode.l10n.t(
                            "Mod project '{0}' created.",
                            name.trim(),
                        ),
                    );
                }
            },
        ),
    );

    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.reloadProject",
            async () => {
                const projectKey = await commandProjectKey();
                if (projectKey === null) {
                    return;
                }
                await gatewayFor(projectKey).executeCommand(
                    "aet-eaw-edit.lsp.reloadProject",
                    [],
                    "could not reload the project",
                );
            },
        ),
    );

    // The server's remaining commands, owned here like the rest. The language client would register
    // them from the server's one `registerCapability` batch, but that batch also names commands this
    // extension registers itself, collides, and is dropped whole - so a quick fix naming one of these
    // found no command at all.
    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.suppressDiagnosticGlobally",
            async (...args: unknown[]) => {
                const projectKey = await commandProjectKey();
                if (projectKey === null) {
                    return;
                }
                await gatewayFor(projectKey).executeCommand(
                    "aet-eaw-edit.lsp.suppressDiagnosticGlobally",
                    args,
                    "could not suppress the diagnostic",
                );
            },
        ),
    );

    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.revalidateDocument",
            async (uri?: string) => {
                const target =
                    uri ??
                    vscode.window.activeTextEditor?.document.uri.toString();
                if (target === undefined) {
                    return;
                }
                const owner = ownerOf(vscode.Uri.parse(target).fsPath);
                const projectKey =
                    owner === undefined || clients.size <= 1
                        ? await commandProjectKey()
                        : owner.spec.key;
                if (projectKey === null) {
                    return;
                }
                await gatewayFor(projectKey).executeCommand(
                    "aet-eaw-edit.lsp.revalidateDocument",
                    [target],
                    "could not revalidate the document",
                );
            },
        ),
    );

    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.createLocalisationKey",
            async (keyName?: string) => {
                if (!keyName) {
                    vscode.window.showWarningMessage(
                        vscode.l10n.t(
                            "EaWEdit: No localisation key name provided.",
                        ),
                    );
                    return;
                }

                // The quick fix raising this belongs to the document in front of the reader.
                const projectKey = await commandProjectKey();
                if (projectKey === null) {
                    return;
                }
                const lsp = gatewayFor(projectKey);
                const fetched =
                    await lsp.request<GetLocalisationProjectsResult>(
                        "aet/getLocalisationProjects",
                    );
                if (!fetched.ok) {
                    vscode.window.showWarningMessage(
                        vscode.l10n.t(
                            "EaWEdit: Could not fetch localisation projects from server.",
                        ),
                    );
                    return;
                }
                const projects = fetched.value.projects ?? [];

                if (!projects.length) {
                    vscode.window.showWarningMessage(
                        vscode.l10n.t(
                            "EaWEdit: No localisation projects found. Use 'EaWEdit: Initialise Localisation Project from Baseline' first.",
                        ),
                    );
                    return;
                }

                const picked = await vscode.window.showQuickPick(
                    projects.map((p) => ({
                        label: p.label,
                        description: p.filePath,
                        detail: `${p.projectName} - ${p.resourceType}`,
                        filePath: p.filePath,
                    })),
                    {
                        title: vscode.l10n.t(
                            "Create localisation key '{0}'",
                            keyName,
                        ),
                        placeHolder: vscode.l10n.t(
                            "Select localisation project",
                        ),
                    },
                );
                if (!picked) {
                    return;
                }

                const english = await vscode.window.showInputBox({
                    title: vscode.l10n.t("New key: {0}", keyName),
                    prompt: vscode.l10n.t(
                        "English translation text (required)",
                    ),
                    validateInput: (v) =>
                        v?.trim()
                            ? null
                            : vscode.l10n.t("Translation text is required"),
                });
                if (english === undefined) {
                    return;
                }

                await lsp.executeCommand(
                    "aet-eaw-edit.lsp.createLocalisationKey",
                    [keyName, picked.filePath, { ENGLISH: english }],
                    `could not create the localisation key '${keyName}'`,
                );
            },
        ),
    );

    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.initLocalisationProject",
            async () => {
                const projectKey = await commandProjectKey();
                if (projectKey === null) {
                    return;
                }
                const lsp = gatewayFor(projectKey);
                if (!lsp.requireRunning()) {
                    return;
                }

                const config =
                    await lsp.request<GetRootLocalisationConfigResult>(
                        "aet/getRootLocalisationConfig",
                    );
                if (!config.ok) {
                    vscode.window.showWarningMessage(
                        vscode.l10n.t(
                            "EaWEdit LSP: Could not query the project's localisation config.",
                        ),
                    );
                    return;
                }
                const rootConfig = config.value;

                // The .pgproj already declares a localisation node - it wins outright, no picker.
                if (rootConfig.configured) {
                    const confirmed =
                        await vscode.window.showInformationMessage(
                            vscode.l10n.t(
                                'Initialise the localisation project ({0} in "{1}")?',
                                rootConfig.type ?? "",
                                rootConfig.directory ?? "",
                            ),
                            { modal: true },
                            vscode.l10n.t("Initialise"),
                        );
                    if (confirmed !== vscode.l10n.t("Initialise")) {
                        return;
                    }
                    await lsp.executeCommand(
                        "aet-eaw-edit.lsp.initLocalisationProject",
                        [{}],
                        "could not initialise the localisation project",
                    );
                    return;
                }

                // Not configured - the VS Code setting only pre-fills the picker here, as a last resort.
                const formatOptions = [
                    {
                        label: vscode.l10n.t("CSV"),
                        description: vscode.l10n.t(
                            "Comma-separated values (.csv)",
                        ),
                    },
                    {
                        label: vscode.l10n.t("XML"),
                        description: vscode.l10n.t(
                            "eaw-translation v1 XML (.xml)",
                        ),
                    },
                    {
                        label: vscode.l10n.t("NLS"),
                        description: vscode.l10n.t(
                            "Java-style properties (.properties)",
                        ),
                    },
                ];
                const defaultFormat = cfg("localisation")
                    .get<string>("format", "format-dat")
                    .replace(/^format-/, "")
                    .toUpperCase();
                const defaultIdx = formatOptions.findIndex(
                    (o) => o.label === defaultFormat,
                );
                if (defaultIdx > 0) {
                    formatOptions.unshift(
                        formatOptions.splice(defaultIdx, 1)[0],
                    );
                }

                const formatItem = await vscode.window.showQuickPick(
                    formatOptions,
                    {
                        title: vscode.l10n.t(
                            "Initialise Localisation Project from Baseline",
                        ),
                        placeHolder: vscode.l10n.t("Select output format"),
                    },
                );
                if (!formatItem) {
                    return;
                }

                const directory = await vscode.window.showInputBox({
                    title: vscode.l10n.t(
                        "Localisation directory (relative to the .pgproj)",
                    ),
                    value: "data/text",
                    prompt: vscode.l10n.t(
                        "Where the localisation project files will live",
                    ),
                    validateInput: (v) =>
                        v?.trim() ? null : "A directory is required",
                });
                if (!directory) {
                    return;
                }

                await lsp.executeCommand(
                    "aet-eaw-edit.lsp.initLocalisationProject",
                    [{ format: formatItem.label, directory: directory.trim() }],
                    "could not initialise the localisation project",
                );
            },
        ),
    );

    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.importLocalisationProject",
            async () => {
                const projectKey = await commandProjectKey();
                if (projectKey === null) {
                    return;
                }
                const lsp = gatewayFor(projectKey);
                if (!lsp.requireRunning()) {
                    return;
                }

                const convertibleFormats = [
                    {
                        label: vscode.l10n.t("CSV"),
                        description: vscode.l10n.t(
                            "Comma-separated values (.csv)",
                        ),
                    },
                    {
                        label: vscode.l10n.t("XML"),
                        description: vscode.l10n.t(
                            "eaw-translation v1 XML (.xml)",
                        ),
                    },
                    {
                        label: vscode.l10n.t("NLS"),
                        description: vscode.l10n.t(
                            "Java-style properties (.properties)",
                        ),
                    },
                ];
                const datFormat = {
                    label: vscode.l10n.t("DAT"),
                    description: vscode.l10n.t(
                        "The game's proprietary binary format (.dat)",
                    ),
                };
                const sourceFormatChoices = [...convertibleFormats, datFormat];

                const sourceFormatItem = await vscode.window.showQuickPick(
                    sourceFormatChoices,
                    {
                        title: vscode.l10n.t(
                            "Import Existing Localisation Files",
                        ),
                        placeHolder: vscode.l10n.t(
                            "Format of the existing files",
                        ),
                    },
                );
                if (!sourceFormatItem) {
                    return;
                }
                const isDatSource =
                    sourceFormatItem.label === vscode.l10n.t("DAT");

                // DAT is one binary file per language (MasterTextFile_<LANGUAGE>.dat) - the user picks
                // one file, the server discovers its siblings in the same folder. Every other format
                // stores all languages in one file, so the user picks the containing folder directly.
                let sourceDirectory: string;
                const defaultUri = vscode.workspace.workspaceFolders?.[0]?.uri;
                if (isDatSource) {
                    const sourceFiles = await vscode.window.showOpenDialog({
                        canSelectFolders: false,
                        canSelectFiles: true,
                        canSelectMany: false,
                        defaultUri,
                        filters: { "DAT files": ["dat"] },
                        openLabel:
                            "Select a MasterTextFile_<LANGUAGE>.dat file",
                        title: vscode.l10n.t(
                            "Select one DAT file - its siblings in the same folder are imported too",
                        ),
                    });
                    if (!sourceFiles?.length) {
                        return;
                    }
                    sourceDirectory = vscode.Uri.joinPath(
                        sourceFiles[0],
                        "..",
                    ).fsPath;
                } else {
                    const sourceFolders = await vscode.window.showOpenDialog({
                        canSelectFolders: true,
                        canSelectFiles: false,
                        canSelectMany: false,
                        defaultUri,
                        openLabel: "Select folder with existing files",
                        title: vscode.l10n.t(
                            "Folder containing the {0} files",
                            sourceFormatItem.label,
                        ),
                    });
                    if (!sourceFolders?.length) {
                        return;
                    }
                    sourceDirectory = sourceFolders[0].fsPath;
                }

                // Pre-fill the target format to match the source - the common case is "just register
                // what I already have," not a conversion. DAT is never a conversion target (no DAT
                // generator wired into this wizard - use the existing "Export DAT" action for that), so
                // it only appears as a target choice when the source is DAT itself, with no pre-selection
                // (DAT is never git-friendly for collaborative editing - force an explicit choice).
                let targetFormatItem:
                    { label: string; description: string } | undefined;
                if (isDatSource) {
                    targetFormatItem = await vscode.window.showQuickPick(
                        [...convertibleFormats, datFormat],
                        {
                            title: vscode.l10n.t("Target Format"),
                            placeHolder: vscode.l10n.t(
                                "Format to use going forward",
                            ),
                        },
                    );
                } else {
                    const targetChoices = convertibleFormats.map((o) => ({
                        ...o,
                    }));
                    const sourceIdx = targetChoices.findIndex(
                        (o) => o.label === sourceFormatItem.label,
                    );
                    if (sourceIdx > 0) {
                        targetChoices.unshift(
                            targetChoices.splice(sourceIdx, 1)[0],
                        );
                    }
                    targetFormatItem = await vscode.window.showQuickPick(
                        targetChoices,
                        {
                            title: vscode.l10n.t("Target Format"),
                            placeHolder: vscode.l10n.t(
                                "Format to use going forward (defaults to {0})",
                                sourceFormatItem.label,
                            ),
                        },
                    );
                }
                if (!targetFormatItem) {
                    return;
                }

                let targetDirectory: string | undefined;
                if (targetFormatItem.label !== sourceFormatItem.label) {
                    targetDirectory = await vscode.window.showInputBox({
                        title: vscode.l10n.t(
                            "Target directory (relative to the .pgproj)",
                        ),
                        value: "data/text",
                        prompt: vscode.l10n.t(
                            "Converting {0} to {1} - where should the new file go?",
                            sourceFormatItem.label,
                            targetFormatItem.label,
                        ),
                        validateInput: (v) =>
                            v?.trim() ? null : "A directory is required",
                    });
                    if (!targetDirectory) {
                        return;
                    }
                }

                await lsp.executeCommand(
                    "aet-eaw-edit.lsp.importLocalisationProject",
                    [
                        {
                            sourceFormat: sourceFormatItem.label,
                            sourceDirectory,
                            targetFormat: targetFormatItem.label,
                            ...(targetDirectory
                                ? { targetDirectory: targetDirectory.trim() }
                                : {}),
                        },
                    ],
                    "could not import the localisation files",
                );
                // No message here: executeCommand returns nothing to check, so anything said at this
                // point is a guess. The server reports the outcome - what it wrote, or why it did not.
            },
        ),
    );

    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.showReferences",
            (
                uriStr: string,
                position: { line: number; character: number },
                rawLocations: {
                    uri: string;
                    range: {
                        start: { line: number; character: number };
                        end: { line: number; character: number };
                    };
                }[],
            ) => {
                const uri = vscode.Uri.parse(uriStr);
                const pos = new vscode.Position(
                    position.line,
                    position.character,
                );
                const locations = rawLocations.map(
                    (l) =>
                        new vscode.Location(
                            vscode.Uri.parse(l.uri),
                            new vscode.Range(
                                new vscode.Position(
                                    l.range.start.line,
                                    l.range.start.character,
                                ),
                                new vscode.Position(
                                    l.range.end.line,
                                    l.range.end.character,
                                ),
                            ),
                        ),
                );
                vscode.commands.executeCommand(
                    "editor.action.showReferences",
                    uri,
                    pos,
                    locations,
                );
            },
        ),
    );

    // Opens the encyclopedia popup preview for a GameObject. Triggered by the "preview encyclopedia"
    // code lens (objectId passed as the first argument), or from the command palette (prompts).
    // Opening a .alo or .ala shows the model instead of the binary editor. Registered whatever the
    // feature flag says: the flag gates the SERVER endpoints, and a custom editor that vanishes with
    // a setting would leave those files opening as binary with no hint why.
    context.subscriptions.push(
        ModelPreviewEditorProvider.register(context, (uri) =>
            panelProject(ownerOf(uri.fsPath)?.spec.key),
        ),
    );

    // The bug report block. Works with the server down too - "not running" is itself the most
    // useful thing a report can say - so it asks rather than requiring a running server.
    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.copyBugReportInfo",
            async () => {
                const tier = await pickReportTier("Copy Bug Report Info");
                if (!tier) {
                    return;
                }

                // The clipboard has no size limit, so the extended copy carries the per-type list too.
                const text = await collectBugReport(
                    context,
                    lsp,
                    tier.extended ? { extended: true, symbolTypes: true } : {},
                );
                await vscode.env.clipboard.writeText(text);
                void vscode.window.showInformationMessage(
                    vscode.l10n.t("EaWEdit: Bug report info copied"),
                );
            },
        ),
    );

    // The two report actions every diagnostic offers beside its suppressions. Client commands the
    // server only names: the version, the clipboard and the browser are all on this side.
    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.reportDiagnosticOnGitHub",
            (report: DiagnosticReportPayload) =>
                reportDiagnosticOnGitHub(context, lsp, report),
        ),
        vscode.commands.registerCommand(
            "aet-eaw-edit.openDiagnosticReport",
            (report: DiagnosticReportPayload) =>
                openDiagnosticReport(context, lsp, report),
        ),
    );

    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.previewModel",
            async (referenceArg?: string) => {
                const projectKey = await commandProjectKey();
                if (projectKey === null) {
                    return;
                }
                const lsp = gatewayFor(projectKey);
                if (!lsp.requireRunning()) {
                    return;
                }

                const name = referenceArg?.trim() || (await pickModel(lsp));
                if (!name) {
                    return;
                }

                ModelPreviewPanel.show(
                    context.extensionUri,
                    panelProject(projectKey),
                    { kind: "model", modelReference: name },
                    name,
                );
            },
        ),
    );

    // Never wired to activation: a modder who has not asked for shader-accurate previews should not
    // be nagged about a download they may not want.
    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.shaders.obtain",
            async () => {
                await offerShaderSources(context);
            },
        ),
    );

    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.previewAssembledUnit",
            async (objectIdArg?: string) => {
                const projectKey = await commandProjectKey();
                if (projectKey === null) {
                    return;
                }
                const lsp = gatewayFor(projectKey);
                if (!lsp.requireRunning()) {
                    return;
                }

                let objectId = objectIdArg;
                if (!objectId) {
                    objectId = await vscode.window.showInputBox({
                        title: vscode.l10n.t("Preview Assembled Unit"),
                        prompt: vscode.l10n.t(
                            "Name of the GameObject to assemble, e.g. Generic_Star_Destroyer",
                        ),
                        validateInput: (v) =>
                            v?.trim() ? null : "Object name is required",
                    });
                }
                if (!objectId?.trim()) {
                    return;
                }

                const id = objectId.trim();
                ModelPreviewPanel.show(
                    context.extensionUri,
                    panelProject(projectKey),
                    { kind: "object", objectId: id },
                    id,
                );
            },
        ),
    );

    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.showEncyclopedia",
            async (objectIdArg?: string) => {
                const projectKey = await commandProjectKey();
                if (projectKey === null) {
                    return;
                }
                const lsp = gatewayFor(projectKey);
                if (!lsp.requireRunning()) {
                    return;
                }

                let objectId = objectIdArg;
                if (!objectId) {
                    objectId = await vscode.window.showInputBox({
                        title: vscode.l10n.t("Preview Encyclopedia Popup"),
                        prompt: vscode.l10n.t(
                            "Name of the GameObject whose popup to preview",
                        ),
                        validateInput: (v) =>
                            v?.trim() ? null : "Object name is required",
                    });
                }
                if (!objectId?.trim()) {
                    return;
                }
                const id = objectId.trim();

                // Both the first open and the SP/MP toggle go through here, so the panel never has to
                // know how to reach the server - it asks, this fetches.
                const load = async (
                    multiplayer: boolean,
                    retarget: boolean,
                ): Promise<void> => {
                    const entry =
                        await lsp.requestOrReport<GetEncyclopediaEntryResult>(
                            GET_ENCYCLOPEDIA_ENTRY,
                            { objectId: id, multiplayer },
                            "load the encyclopedia entry",
                        );
                    if (entry === undefined) {
                        return;
                    }

                    if (retarget) {
                        EncyclopediaPanel.show(
                            context.extensionUri,
                            panelProject(projectKey),
                            entry,
                            (mp) => void load(mp, false),
                        );
                    } else {
                        EncyclopediaPanel.update(
                            resolveProjectKey(projectKey),
                            entry,
                        );
                    }
                };

                // Retargeting keeps whatever SP/MP mode the panel is already showing.
                await load(
                    EncyclopediaPanel.multiplayer(
                        resolveProjectKey(projectKey),
                    ),
                    true,
                );
            },
        ),
    );

    // Opens the read-only "effective object" virtual document for a variant GameObject. Triggered by
    // the "show effective object" code lens (objectId passed as the first argument), or from the command
    // palette (prompts for the object name).
    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.showEffectiveObject",
            async (objectIdArg?: string) => {
                const projectKey = await commandProjectKey();
                if (projectKey === null) {
                    return;
                }
                const lsp = gatewayFor(projectKey);
                if (!lsp.requireRunning()) {
                    return;
                }

                let objectId = objectIdArg;
                if (!objectId) {
                    objectId = await vscode.window.showInputBox({
                        title: vscode.l10n.t("Show Effective Object"),
                        prompt: vscode.l10n.t(
                            "Name of the variant GameObject to resolve",
                        ),
                        validateInput: (v) =>
                            v?.trim() ? null : "Object name is required",
                    });
                }
                if (!objectId?.trim()) {
                    return;
                }
                objectId = objectId.trim();

                // The tab label is derived from the URI's path basename, so bake the intent into it: this is a
                // generated, read-only preview of the fully-merged object, not an editable source file. The real
                // object id and its project travel in the query, so the decorative basename doesn't affect
                // content resolution.
                const project = panelProject(projectKey);
                const uri = vscode.Uri.from({
                    scheme: EFFECTIVE_SCHEME,
                    path: effectiveObjectPath(
                        objectId,
                        project.name,
                        clients.size,
                    ),
                    query: effectiveObjectQuery({
                        objectId,
                        projectKey: project.key,
                    }),
                });
                effectiveObjectProvider?.refresh(uri);
                const doc = await vscode.workspace.openTextDocument(uri);
                // Open beside the active editor so the preview sits next to the source it was generated from.
                await vscode.window.showTextDocument(doc, {
                    preview: true,
                    viewColumn: vscode.ViewColumn.Beside,
                });
            },
        ),
    );

    if (
        cfg("lsp").get<boolean>("enabled") === true &&
        validateConfiguration(context.extensionPath)
    ) {
        logLine("LSP enabled - starting server.");
        void reconcileClients(context);
    } else if (cfg("lsp").get<boolean>("enabled") !== true) {
        logLine(
            "LSP disabled (aet-eaw-edit.lsp.enabled is false). Use the restart command or enable it in settings.",
        );
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
    await stopAllClients();
}
