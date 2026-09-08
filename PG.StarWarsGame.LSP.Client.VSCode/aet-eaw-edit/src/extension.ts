// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import {
	ClientCapabilities,
	FeatureState,
	LanguageClient,
	LanguageClientOptions,
	ServerOptions,
	StaticFeature,
	Trace,
	TransportKind,
} from 'vscode-languageclient/node';
import { CreditsPreviewPanel } from './creditsPreviewPanel';
import { EncyclopediaPanel } from './encyclopediaPanel';
import { ModelPreviewEditorProvider } from './modelPreviewEditor';
import { ModelInspectorPanel } from './modelInspectorPanel';
import { ModelPreviewPanel } from './modelPreviewPanel';
import { offerShaderSources, shaderDirectory } from './shaderSources';
import { initDialogGeometryStorage } from './dialogGeometryStorage';
import { initPanelLayoutStorage } from './panelLayoutStorage';
import { initViewerSettingsStorage } from './viewerSettingsStorage';
import { initProjectSettingsStorage } from './projectSettingsStorage';
import { LocalisationEditorPanel } from './localisationEditorPanel';
import { LocalisationNavigatorViewProvider, LocTreeItem } from './localisationNavigatorViewProvider';
import { LspGateway } from './lsp/lspGateway';
import { vscodeMessageSink } from './lsp/vscodeMessageSink';
import {
    ConvertLocalisationFormatResult, ExportLocalisationToDatResult, GetEffectiveObjectResult,
    GetEncyclopediaEntryResult, GetLocalisationProjectsResult, GetRootLocalisationConfigResult,
    GetStoryPlotsResult, LOC_CATEGORY, LocProjectInfo, StoryGraphChangedParams,
    StorySimChangedParams,
} from './protocol';
import { StoryGraphPanel } from './storyGraphPanel';
import { StoryNavigatorViewProvider, StoryTreeItem } from './storyNavigatorViewProvider';

const CLIENT_ID = 'aet.pg.swg.lsp';
const CLIENT_NAME = 'Alamo Engine Tools - Empire at War Edit';
const RELEASES_URL = 'https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/releases';
const REQUIRED_SERVER_VERSION = '0.3.0';

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
		'synchronization',
		'completion',
		'hover',
		'signatureHelp',
		'declaration',
		'definition',
		'typeDefinition',
		'implementation',
		'references',
		'documentHighlight',
		'documentSymbol',
		'codeAction',
		'codeLens',
		'documentLink',
		'rename',
		'foldingRange',
		'selectionRange',
		'linkedEditingRange',
		'onTypeFormatting',
		'inlayHint',
	] as const;

	fillClientCapabilities(capabilities: ClientCapabilities): void {
		const textDocument = (capabilities.textDocument ??= {}) as Record<string, { dynamicRegistration?: boolean }>;
		for (const key of ForceStaticCapabilitiesFeature.capabilityKeys) {
			const capability = (textDocument[key] ??= {});
			capability.dynamicRegistration = false;
		}
	}

	initialize(): void {
		// no-op: this feature only adjusts the advertised client capabilities.
	}

	getState(): FeatureState {
		return { kind: 'static' };
	}

	clear(): void {
		// no-op
	}
}

/** URI scheme for the read-only "effective object" virtual documents (variant inheritance). */
const EFFECTIVE_SCHEME = 'aet-effective';

/**
 * A leading XML comment banner clarifying that the document is a generated, read-only preview of the
 * fully-merged object - reinforcing what the tab title already says, for when only the body is visible.
 * ASCII only by house rule.
 */
function effectiveObjectBanner(objectId: string): string {
	return [
		'<!--',
		'  ============================================================',
		'  GENERATED READ-ONLY PREVIEW',
		`  Effective (fully-merged) form of variant object '${objectId}'.`,
		'  This is not an editable source file - edits are discarded, and',
		'  go-to-definition is not available here.',
		'  ============================================================',
		'-->',
		'',
	].join('\n');
}

/**
 * Serves the merged "effective" form of a variant GameObject as a read-only virtual XML document.
 * The object id is carried in the URI query; content is fetched from the server via
 * `aet/getEffectiveObject`. Read-only is implicit for TextDocumentContentProvider documents.
 */
class EffectiveObjectContentProvider implements vscode.TextDocumentContentProvider {
	private readonly _onDidChange = new vscode.EventEmitter<vscode.Uri>();
	readonly onDidChange = this._onDidChange.event;

	/** Signals VS Code to re-fetch content for an already-open virtual document. */
	refresh(uri: vscode.Uri): void {
		this._onDidChange.fire(uri);
	}

	async provideTextDocumentContent(uri: vscode.Uri): Promise<string> {
		const objectId = uri.query || uri.path.replace(/^\//, '').replace(/\.xml$/i, '');

		// The document IS the report here - it is read-only generated text, so a failure is written
		// into it as a comment rather than raised as a notification over the tab that just opened.
		const outcome = await lsp.request<GetEffectiveObjectResult>(
			'aet/getEffectiveObject', { objectId });

		if (!outcome.ok) {
			return outcome.reason === 'offline'
				? '<!-- EaWEdit: LSP server is not running. -->'
				: `<!-- EaWEdit: Failed to resolve effective object '${objectId}' - ${outcome.message} -->`;
		}
		if (!outcome.value.found) {
			return `<!-- EaWEdit: No object named '${objectId}' was found in the workspace. -->`;
		}
		return effectiveObjectBanner(objectId) + outcome.value.xml;
	}
}

let lspClient: LanguageClient | undefined;

/**
 * Every request to the server goes through here.
 *
 * Resolves `lspClient` per call rather than holding it: the client is replaced on every restart,
 * so anything that captured one would keep talking to a dead process. Built once at module scope
 * because the panels and navigators are handed it at construction and outlive any single client.
 */
const lsp = new LspGateway(() => lspClient, vscodeMessageSink);

let effectiveObjectProvider: EffectiveObjectContentProvider | undefined;
let localisationNavigatorProvider: LocalisationNavigatorViewProvider | undefined;
let storyNavigatorProvider: StoryNavigatorViewProvider | undefined;
let statusItem: vscode.StatusBarItem | undefined;
let traceChannel: vscode.LogOutputChannel | undefined;
let log: vscode.OutputChannel | undefined;

function logLine(msg: string): void {
	const ts = new Date().toISOString().replace('T', ' ').replace('Z', '');
	log?.appendLine(`[${ts}] ${msg}`);
}

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
	const write = (message: string, ..._args: unknown[]): void => channel.appendLine(message);
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
function gameDirectory(name: 'baseGameDirectory' | 'expansionDirectory'): string | undefined {
	return cfg('lsp.source').get<string>(name)
		|| cfg('modVerify').get<string>(name)
		|| undefined;
}

/**
 * Builds the complete resolved feature-flag object sent to the server via initializationOptions.
 * The server's FeatureFlags record (Core\Configuration\FeatureFlags.cs) defaults everything to
 * true; the user-facing off-defaults (lua.hover, lua.diagnostics, tools.localisation,
 * story.discovery) live in package.json, so the fallbacks here must mirror package.json. The one
 * exception is tools.storySimulator, which is intentionally not contributed at all - its fallback
 * below is the only default it has. Flags are restart-based: the config listener in activate()
 * restarts the server when any `aet-eaw-edit.features` value changes.
 */
function resolveFeatureFlags() {
	const features = cfg('features');
	const flag = (key: string, fallback: boolean) => features.get<boolean>(key, fallback);
	return {
		xml: {
			completion:     flag('xml.completion', true),
			hover:          flag('xml.hover', true),
			diagnostics:    flag('xml.diagnostics', true),
			goToDefinition: flag('xml.goToDefinition', true),
			findReferences: flag('xml.findReferences', true),
			rename:         flag('xml.rename', true),
			codeLens:       flag('xml.codeLens', true),
			inlayHints:     flag('xml.inlayHints', true),
			codeActions:    flag('xml.codeActions', true),
			linkedEditing:  flag('xml.linkedEditing', true),
			autoCloseTag:   flag('xml.autoCloseTag', true),
		},
		lua: {
			completion:     flag('lua.completion', true),
			hover:          flag('lua.hover', false),
			diagnostics:    flag('lua.diagnostics', false),
			goToDefinition: flag('lua.goToDefinition', true),
			rename:         flag('lua.rename', true),
			codeLens:       flag('lua.codeLens', true),
			inlayHints:     flag('lua.inlayHints', true),
			codeActions:    flag('lua.codeActions', true),
		},
		tools: {
			localisation:   flag('tools.localisation', false),
			storyEditor:    flag('tools.storyEditor', false),
			storyEditing:   flag('tools.storyEditing', false),
			// Deliberately NOT contributed in package.json: Simulation mode is unfinished, so it is
			// kept out of the settings UI. Writing the key into settings.json by hand still works
			// (VS Code returns undeclared values, and the features-wide restart listener still
			// fires) - that is the escape hatch for trying it out.
			storySimulator: flag('tools.storySimulator', false),
			variants:       flag('tools.variants', true),
			encyclopedia:   flag('tools.encyclopedia', true),
			modelPreview:   flag('tools.modelPreview', true),
		},
		story: {
			discovery:        flag('story.discovery', false),
			graphDiagnostics: flag('story.graphDiagnostics', false),
			symbols:          flag('story.symbols', false),
			rename:           flag('story.rename', false),
		},
		dialog: {
			diagnostics:    flag('dialog.diagnostics', false),
			inlayHints:     flag('dialog.inlayHints', false),
			goToDefinition: flag('dialog.goToDefinition', false),
			codeActions:    flag('dialog.codeActions', false),
		},
	};
}

function validateConfiguration(): boolean {
	if (!cfg('lsp').get<boolean>('enabled')) {
		return true;
	}

	let valid = true;
	const devMode = cfg('lsp.devMode').get<boolean>('enabled', false);

	if (!devMode) {
		const serverExePath = cfg('lsp').get<string>('executable');
		if (!serverExePath) {
			void vscode.window.showErrorMessage(
				'EaWEdit: LSP is enabled but no server executable is configured. ' +
				`Download version ${REQUIRED_SERVER_VERSION} from the releases page, ` +
				'then set "aet-eaw-edit.lsp.executable".',
				'Download'
			).then(choice => {
				if (choice === 'Download') {
					void vscode.env.openExternal(vscode.Uri.parse(RELEASES_URL));
				}
			});
			valid = false;
		} else if (!fs.existsSync(serverExePath)) {
			void vscode.window.showErrorMessage(
				`EaWEdit: LSP server not found at "${serverExePath}". ` +
				`Download version ${REQUIRED_SERVER_VERSION} from the releases page.`,
				'Download'
			).then(choice => {
				if (choice === 'Download') {
					void vscode.env.openExternal(vscode.Uri.parse(RELEASES_URL));
				}
			});
			valid = false;
		}
	}

	if (cfg('lsp.source.baseline').get<string>('type') === 'local' &&
		!cfg('lsp.source.baseline').get<string>('localPath')) {
		vscode.window.showErrorMessage(
			'EaWEdit: Baseline type is "local" but no path is configured. ' +
			'Set "aet-eaw-edit.lsp.source.baseline.localPath".'
		);
		valid = false;
	}

	if (cfg('lsp.schema').get<string>('source') === 'local' &&
		!cfg('lsp.schema').get<string>('localPath')) {
		vscode.window.showErrorMessage(
			'EaWEdit: Schema source is "local" but no directory is configured. ' +
			'Set "aet-eaw-edit.lsp.schema.localPath".'
		);
		valid = false;
	}

	return valid;
}

async function startLspClient(context: vscode.ExtensionContext): Promise<void> {
	let serverExe = 'dotnet';
	const devMode = cfg('lsp.devMode').get<boolean>('enabled', false);
	const waitForDebugger = cfg('lsp.debug').get<boolean>('waitForDebugger', false);

	let serverArgs: string[];
	if (devMode) {
		const projectPath = cfg('lsp.devMode').get<string>('projectPath') ||
			path.join(context.extensionPath, '..', '..', 'PG.StarWarsGame.LSP.Server', 'PG.StarWarsGame.LSP.Server.csproj');
		serverArgs = waitForDebugger
			? ['run', '--project', projectPath, '--', '--wait-for-debugger']
			: ['run', '--project', projectPath];
		logLine(`Dev mode: Starting from source - ${serverExe} ${serverArgs.join(' ')}`);
	} else {
		const serverPath = cfg('lsp').get<string>('executable')!;
		// .dll → framework-dependent: launch via `dotnet <path>`.
		// Anything else (self-contained .exe / Linux binary) → invoke directly.
		const isDll = serverPath.toLowerCase().endsWith('.dll');
		serverExe = isDll ? 'dotnet' : serverPath;
		serverArgs = isDll
			? (waitForDebugger ? [serverPath, '--wait-for-debugger'] : [serverPath])
			: (waitForDebugger ? ['--wait-for-debugger'] : []);
		logLine(`Starting LSP server: ${serverExe} ${serverArgs.join(' ')}`);
	}

	const serverOptions: ServerOptions = {
		run:   { command: serverExe, args: serverArgs, transport: TransportKind.stdio },
		debug: { command: serverExe, args: serverArgs, transport: TransportKind.stdio },
	};

	const schemaSource = cfg('lsp.schema').get<string>('source', 'http');

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
	const fileEvents = ['**/*.xml', '**/*.lua', '**/*.pgproj', '**/*.csv', '**/*.properties', '**/*.dat']
		.map(glob => vscode.workspace.createFileSystemWatcher(glob));
	// The client disposes what it is given, but only on a clean shutdown; tying them to the
	// extension's own lifetime means a failed start cannot leak OS watchers.
	context.subscriptions.push(...fileEvents);

	const clientOptions: LanguageClientOptions = {
		synchronize: { fileEvents },
		documentSelector: [
			{ scheme: 'file', language: 'xml' },
			{ scheme: 'file', language: 'lua' },
			// Story-dialog scripts open as plaintext; the server gates all work on the
			// pgproj storyDialog registry scope, so other .txt files stay untouched.
			{ scheme: 'file', language: 'plaintext', pattern: '**/*.txt' },
		],
		traceOutputChannel: traceChannel,
		initializationOptions: {
			workspaceRoot:     vscode.workspace.workspaceFolders?.[0]?.uri.fsPath,
			baseGamePath:      gameDirectory('baseGameDirectory'),
			// The user's own copy of the base shader SOURCES; never shipped with this extension.
			shaderPath:        shaderDirectory(),
			expansionGamePath: gameDirectory('expansionDirectory'),
			locale:            cfg('lsp').get<string>('locale', 'en'),
			// The game's language, not this extension's. `locale` above sets the language the server
			// writes its own hover text and diagnostics in; this one picks the string table.
			localisationLanguage: cfg('lsp.localisation').get<string>('language', 'ENGLISH'),
			schemaUrl:         schemaSource === 'http' ? (cfg('lsp.schema').get<string>('url') || undefined) : undefined,
			schemaLocalPath:   schemaSource === 'local' ? (cfg('lsp.schema').get<string>('localPath') || undefined) : undefined,
			baselineType:      cfg('lsp.source.baseline').get<string>('type', 'http'),
			baselineUrl:       cfg('lsp.source.baseline').get<string>('type', 'http') === 'http'
			                       ? (cfg('lsp.source.baseline').get<string>('url') || undefined)
			                       : undefined,
			baselineLocalPath: cfg('lsp.source.baseline').get<string>('localPath') || undefined,
			features:          resolveFeatureFlags()
		},
		middleware: {
			executeCommand: async (command, args, next) => {
				if (command !== 'aet-eaw-edit.lsp.createLocalisationKey') {
					return next(command, args);
				}

				const keyName = (args[0] as string | undefined) ?? '';
				if (!keyName) {
					vscode.window.showWarningMessage('EaWEdit: No localisation key name provided.');
					return;
				}

				const fetched = await lsp.request<GetLocalisationProjectsResult>(
					'aet/getLocalisationProjects');
				if (!fetched.ok) {
					vscode.window.showWarningMessage(
						'EaWEdit: Could not fetch localisation projects from server.');
					return;
				}
				const projects = fetched.value.projects ?? [];

				if (!projects.length) {
					vscode.window.showWarningMessage(
						"EaWEdit: No localisation projects found. Use 'EaWEdit: Initialise Localisation Project from Baseline' first.");
					return;
				}

				const picked = await vscode.window.showQuickPick(
					projects.map(p => ({
						label: p.label, description: p.filePath,
						detail: `${p.projectName} - ${p.resourceType}`, filePath: p.filePath
					})),
					{ title: `Create localisation key '${keyName}'`, placeHolder: 'Select localisation project' }
				);
				if (!picked) { return; }

				const english = await vscode.window.showInputBox({
					title: `New key: ${keyName}`,
					prompt: 'English translation text (required)',
					validateInput: v => (v?.trim() ? null : 'Translation text is required'),
				});
				if (english === undefined) { return; }

				await next(command, [keyName, picked.filePath, { ENGLISH: english }]);
			},
		},
	};

	lspClient = new LanguageClient(CLIENT_ID, CLIENT_NAME, serverOptions, clientOptions);

	// Must be registered before start(): forces every language-feature capability to be advertised
	// statically, so registration is deterministic and immune to the dynamic-batch abort (and so
	// already-open documents get a didOpen). Otherwise a random subset of features may fail.
	lspClient.registerFeature(new ForceStaticCapabilitiesFeature());

	if (statusItem) {
		statusItem.text = '$(loading~spin) EaWEdit LSP: Starting...';
		statusItem.show();
	}

	const traceLevel = cfg('lsp.debug').get<string>('traceServer', 'off');
	const traceMap: Record<string, Trace> = { off: Trace.Off, messages: Trace.Messages, verbose: Trace.Verbose };
	const resolvedTrace = traceMap[traceLevel] ?? Trace.Off;

	lspClient.start().then(async () => {
		// setTrace must be called after start() resolves: before that, activeConnection()
		// returns undefined and the trace level is stored but never applied to the transport.
		await lspClient?.setTrace(resolvedTrace);
		logLine('LSP server started and initialized.');

		// Anything already waiting on a server can go now. A model preview restored when the window
		// opened is resolved by VS Code long before this point, and without being told it would sit
		// blank for ever on the one request it made and lost.
		lsp.markReady();

		const serverVersion = lspClient?.initializeResult?.serverInfo?.version;
		logLine(`Server version reported: ${serverVersion ?? '(none)'}`);
		if (serverVersion !== REQUIRED_SERVER_VERSION) {
			const msg = serverVersion
				? `EaWEdit: Server version ${serverVersion} does not match the expected version ${REQUIRED_SERVER_VERSION}. Some features may not work correctly.`
				: `EaWEdit: The server did not report a version. Download version ${REQUIRED_SERVER_VERSION} from the releases page.`;
			void vscode.window.showWarningMessage(msg, 'Download').then(choice => {
				if (choice === 'Download') {
					void vscode.env.openExternal(vscode.Uri.parse(RELEASES_URL));
				}
			});
		}

		// vscode-languageclient's hookConfigurationChanged reads aet.pg.swg.lsp.trace.server
		// (the CLIENT_ID namespace), which is not in settings, so it resets trace to Off on
		// every VS Code config change. Register our own listener - after hookConfigurationChanged's
		// listener, so it fires last and wins - to re-apply the user's actual setting.
		context.subscriptions.push(
			vscode.workspace.onDidChangeConfiguration(() => {
				const level = cfg('lsp.debug').get<string>('traceServer', 'off');
				void lspClient?.setTrace(traceMap[level] ?? Trace.Off);
			})
		);
	}).catch((e: unknown) => {
		logLine(`LSP server failed to start: ${e}`);
		lspClient = undefined;
		lsp.markStopped();
		if (statusItem) {
			statusItem.text = '$(error) EaWEdit LSP: Failed to start';
		}
		void vscode.window.showErrorMessage(
			'EaWEdit: LSP server failed to start. Check the EaWEdit output channel for details.',
			'Show Output',
			'Download'
		).then(choice => {
			if (choice === 'Show Output') {
				log?.show(true);
			} else if (choice === 'Download') {
				void vscode.env.openExternal(vscode.Uri.parse(RELEASES_URL));
			}
		});
	});

	lspClient.onNotification('$/workspaceScanComplete', () => {
		logLine('Workspace scan complete.');
		if (statusItem) {
			statusItem.text = '$(check) EaWEdit LSP';
		}
		// Both navigators fetch now rather than when they are first opened. A tree view only asks
		// for its children on reveal, so without this the first click on either paid for a round
		// trip - and until the scan finished they had nothing to show but a "loading" line.
		void storyNavigatorProvider?.preload();
		void localisationNavigatorProvider?.preload();
	});

	lspClient.onNotification('aet/localisationIndexUpdated', () => {
		logLine('Localisation index updated - refreshing localisation views.');
		localisationNavigatorProvider?.refresh();
		// Open tabs decide for themselves whether to re-read: one with staged edits must not be
		// reloaded out from under the user. The notification fires on our own saves too.
		LocalisationEditorPanel.invalidateAll();
	});

	lspClient.onNotification('aet/storyGraphChanged', (params: StoryGraphChangedParams) => {
		logLine(`Story graph changed: ${params.campaigns.join(', ')} - refreshing story views.`);
		storyNavigatorProvider?.refresh();
		StoryGraphPanel.refreshInvalidated(params.campaigns ?? []);
	});

	lspClient.onNotification('aet/storySimChanged', (params: StorySimChangedParams) => {
		StoryGraphPanel.simChanged(params.campaign);
	});

	lspClient.onNotification('aet/previewSceneChanged', () => {
		// Every open preview asks; the ones whose scene did not actually change drop the answer.
		// The server cannot say which previews an edit touches - a scene is assembled from a
		// variant chain, its hardpoints, their models and their weapons, and none of that is
		// tracked back to the files it came from.
		ModelPreviewPanel.refreshAll();
	});
}

async function stopLspClient(): Promise<void> {
	if (lspClient) {
		logLine('Stopping LSP server.');
		await lspClient.stop();
		lspClient = undefined;
		lsp.markStopped();
	}
}

export async function activate(context: vscode.ExtensionContext): Promise<void> {

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

	localisationNavigatorProvider = new LocalisationNavigatorViewProvider(lsp);
	context.subscriptions.push(
		vscode.window.registerTreeDataProvider(
			LocalisationNavigatorViewProvider.viewId, localisationNavigatorProvider),
		vscode.commands.registerCommand('aet-eaw-edit.lsp.refreshLocalisationNavigator',
			() => localisationNavigatorProvider?.refresh()),
		// Hosted here rather than in the tree so the palette can reach it too. Lifted out of the
		// sidebar webview, which owned this quick pick before the tree existed.
		vscode.commands.registerCommand('aet-eaw-edit.lsp.newLocalisationProject', async () => {
			const choice = await vscode.window.showQuickPick(
				[
					{ label: 'Initialise from the game baseline', command: 'aet-eaw-edit.lsp.initLocalisationProject' },
					{ label: 'Import existing localisation files', command: 'aet-eaw-edit.lsp.importLocalisationProject' },
				],
				{ placeHolder: 'Create a localisation project' });
			if (choice) { await vscode.commands.executeCommand(choice.command); }
		}),
		// Opens the localisation grid in its own tab. Invoked with the file tree item, or without
		// arguments from the command palette (quick-picks a file, grouped by category).
		vscode.commands.registerCommand('aet-eaw-edit.lsp.openLocalisationEditor',
			async (arg?: LocTreeItem) => {
				if (!lsp.requireRunning()) { return; }

				// The quick pick below is for the palette, where nothing was clicked. A node that
				// WAS clicked and carries no project is a node that should not have offered this
				// action - answering it by listing every localisation file in the workspace is how
				// clicking a credits group ended up offering unrelated .dat translation files.
				if (arg !== undefined && arg.project === undefined) {
					vscode.window.showWarningMessage(
						'EaWEdit: That node groups several files rather than being one. '
						+ 'Open one of the files under it.');
					return;
				}

				let project = arg?.project;
				if (!project) {
					const result = await lsp.requestOrReport<GetLocalisationProjectsResult>(
						'aet/getLocalisationProjects', {}, 'could not list localisation files');
					if (!result) { return; }
					if (result.error || !result.projects?.length) {
						vscode.window.showWarningMessage(
							`EaWEdit: ${result.error ?? 'no localisation files found in this workspace.'}`);
						return;
					}

					const picked = await vscode.window.showQuickPick(
						result.projects
							.slice()
							.sort((a, b) => a.category.localeCompare(b.category) || a.label.localeCompare(b.label))
							.map(p => ({
								label: p.label,
								description: p.category === LOC_CATEGORY.credits ? 'Credits' : 'Text',
								detail: p.filePath,
								project: p,
							})),
						{ placeHolder: 'Open a localisation file' });
					if (!picked) { return; }
					project = picked.project;
				}

				// A node standing for a set of single-language files opens them as one table, so a
				// translation sits beside its source. A language inside that set opens the same
				// table with the other columns hidden - the set is the project either way, which is
				// why both are named after the set rather than after whichever file was clicked.
				LocalisationEditorPanel.showSet(
					arg?.setFilePaths ?? [project.filePath],
					arg?.setLabel ?? project.label,
					project.category, context.extensionUri, lsp,
					arg?.focusLanguage);
			}),
		// The crawl lives in the editor title bar, where a preview belongs - the same place a
		// Markdown or LaTeX editor puts one. Both are gated to the credits panel by its view type.
		vscode.commands.registerCommand('aet-eaw-edit.lsp.previewCreditsCrawl',
			() => LocalisationEditorPanel.previewCrawl(false)),
		vscode.commands.registerCommand('aet-eaw-edit.lsp.previewCreditsCrawlFullScreen',
			() => LocalisationEditorPanel.previewCrawl(true)),
		// Export was only ever reachable from inside the old sidebar webview; the tree is the
		// natural home for it, and a menu entry needs a real command behind it.
		// Opening the raw file is a thing you do to a file, so it belongs on the file's own menu in
		// the tree rather than inside the editor showing it.
		vscode.commands.registerCommand('aet-eaw-edit.lsp.openLocalisationAsText',
			async (arg?: LocTreeItem) => {
				const filePath = arg?.project?.filePath;
				if (!filePath) {
					vscode.window.showWarningMessage('EaWEdit: No localisation file selected.');
					return;
				}
				await vscode.window.showTextDocument(
					vscode.Uri.file(filePath), { viewColumn: vscode.ViewColumn.Beside });
			}),
		// Convert reaches here from two places - the editor's dock and the palette - so the request
		// and the way its outcome is reported live in one place rather than two that drift.
		vscode.commands.registerCommand('aet-eaw-edit.lsp.convertLocalisationFormat',
			async (arg?: LocTreeItem | { filePath?: string; targetFormat?: string; category?: string }) => {
				if (!lsp.requireRunning()) { return; }

				const direct = arg as { filePath?: string; targetFormat?: string; category?: string } | undefined;
				let filePath = direct?.filePath ?? (arg as LocTreeItem | undefined)?.project?.filePath;
				let category = direct?.category ?? (arg as LocTreeItem | undefined)?.project?.category;

				if (!filePath) {
					const result = await lsp.requestOrReport<GetLocalisationProjectsResult>(
						'aet/getLocalisationProjects', {}, 'could not list localisation files');
					if (!result) { return; }
					if (result.error || !result.projects?.length) {
						vscode.window.showWarningMessage(
							`EaWEdit: ${result.error ?? 'no localisation files found in this workspace.'}`);
						return;
					}
					const picked = await vscode.window.showQuickPick(
						result.projects.map(p => ({
							label: p.label, description: p.resourceType.toUpperCase(), detail: p.filePath, project: p,
						})),
						{ placeHolder: 'Convert which localisation file?' });
					if (!picked) { return; }
					filePath = picked.project.filePath;
					category = picked.project.category;
				}

				let targetFormat = direct?.targetFormat;
				if (!targetFormat) {
					// DAT is absent on purpose: it is the game's load format, produced by Export to DAT.
					const picked = await vscode.window.showQuickPick(
						[
							{ label: 'CSV', description: 'Comma-separated values (.csv)' },
							{ label: 'XML', description: 'eaw-translation v1 XML (.xml)' },
							{ label: 'NLS', description: 'Java-style properties (.properties)' },
						],
						{ title: 'Convert Localisation File', placeHolder: 'Write it in which format?' });
					if (!picked) { return; }
					targetFormat = picked.label;
				}

				const result = await lsp.requestOrReport<ConvertLocalisationFormatResult>(
					'aet/convertLocalisationFormat',
					{ projectFilePath: filePath, targetFormat },
					`could not convert ${path.basename(filePath)} to ${targetFormat}`);
				if (!result) { return; }

				if (result.error) {
					vscode.window.showErrorMessage(`EaWEdit: ${result.error}`);
					return;
				}

				// A repoint leaves everything still in the old format unloaded. Worth saying now,
				// while it is one line of the .pgproj away from being put back.
				const orphans = result.projectFormatChanged && result.otherFilesInOldFormat > 0
					? ` ${result.otherFilesInOldFormat} other file(s) are still in the previous format`
					+ ' and are no longer loaded.'
					: '';
				// A single-language target (NLS) writes one file per language rather than dropping
				// all but one, so the message has to be able to report more than a single path.
				const written = result.writtenPaths ?? (result.writtenPath ? [result.writtenPath] : []);
				const wrote = written.length > 1
					? `wrote ${written.length} files, one per language: ${written.map(p => path.basename(p)).join(', ')}`
					: `wrote ${written[0]}`;
				const choice = await vscode.window.showInformationMessage(
					`EaWEdit: ${wrote}. The original file was kept.${orphans}`,
					'Open');
				localisationNavigatorProvider?.refresh();
				if (choice === 'Open' && result.writtenPath) {
					LocalisationEditorPanel.show(
						result.writtenPath, path.basename(result.writtenPath),
						category ?? LOC_CATEGORY.text, context.extensionUri, lsp);
				}
			}),
		vscode.commands.registerCommand('aet-eaw-edit.lsp.exportLocalisationToDat',
			async (arg?: LocTreeItem) => {
				const filePath = arg?.project?.filePath;
				if (!filePath) {
					vscode.window.showWarningMessage('EaWEdit: No localisation file selected.');
					return;
				}
				if (!lsp.requireRunning()) { return; }

				const result = await lsp.requestOrReport<ExportLocalisationToDatResult>(
					'aet/exportLocalisationToDat', { projectFilePath: filePath }, 'DAT export failed');
				if (!result) { return; }

				if (result.error) {
					vscode.window.showErrorMessage(`EaWEdit: DAT export failed - ${result.error}`);
					return;
				}
				vscode.window.showInformationMessage(
					`EaWEdit: Exported ${result.writtenFiles.length} DAT ${result.writtenFiles.length === 1 ? 'file' : 'files'}.`);
			})
	);

	effectiveObjectProvider = new EffectiveObjectContentProvider();
	context.subscriptions.push(
		vscode.workspace.registerTextDocumentContentProvider(EFFECTIVE_SCHEME, effectiveObjectProvider)
	);

	storyNavigatorProvider = new StoryNavigatorViewProvider(lsp);
	context.subscriptions.push(
		vscode.window.registerTreeDataProvider(StoryNavigatorViewProvider.viewId, storyNavigatorProvider),
		vscode.commands.registerCommand('aet-eaw-edit.lsp.refreshStoryNavigator',
			() => storyNavigatorProvider?.refresh()),
		// Opens the read-only story graph panel. Invoked with the campaign tree item (inline icon
		// in the navigator) or without arguments from the command palette (quick-picks a campaign).
		vscode.commands.registerCommand('aet-eaw-edit.lsp.openStoryGraph', async (arg?: StoryTreeItem | string) => {
			if (!lsp.requireRunning()) { return; }
			let campaign = typeof arg === 'string' ? arg : arg?.campaignName;
			if (!campaign) {
				const result = await lsp.requestOrReport<GetStoryPlotsResult>(
					'aet/getStoryPlots', {}, 'cannot load story campaigns');
				if (!result) { return; }
				if (result.error) { vscode.window.showWarningMessage(`EaWEdit: ${result.error}`); return; }

				const campaigns = result.campaigns ?? [];
				if (!campaigns.length) {
					vscode.window.showInformationMessage('EaWEdit: No story campaigns found in this workspace.');
					return;
				}
				campaign = await vscode.window.showQuickPick(campaigns.map(c => c.name), {
					title: 'Open Story Graph', placeHolder: 'Select a campaign',
				});
			}
			if (campaign) { StoryGraphPanel.show(campaign, context.extensionUri, lsp); }
		}),
		// Prefer the server-resolved URI: manifest entries and on-disk names differ in casing
		// throughout vanilla data (the engine is case-insensitive, findFiles is not), and the
		// file may live outside the workspace (dependency / base game). The name-based search
		// remains as a fallback for entries the server could not resolve (broken chain links).
		vscode.commands.registerCommand('aet-eaw-edit.lsp.openStoryFile', async (fileName: string, uri?: string) => {
			if (uri) {
				try {
					const doc = await vscode.workspace.openTextDocument(vscode.Uri.parse(uri));
					await vscode.window.showTextDocument(doc, { preview: true });
					return;
				} catch {
					// e.g. stale URI after files changed on disk - fall through to the name search.
				}
			}
			if (!fileName) { return; }
			const matches = await vscode.workspace.findFiles(`**/${fileName}`, '**/node_modules/**', 2);
			if (!matches.length) {
				vscode.window.showWarningMessage(
					`EaWEdit: '${fileName}' was not found in the workspace (it may live in a dependency or the base game).`);
				return;
			}
			const doc = await vscode.workspace.openTextDocument(matches[0]);
			await vscode.window.showTextDocument(doc, { preview: true });
		}),
	);

	statusItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 0);
	statusItem.tooltip = CLIENT_NAME;
	context.subscriptions.push(statusItem);

	log = vscode.window.createOutputChannel('EaWEdit');
	context.subscriptions.push(log);

	traceChannel = createTraceChannel('EaWEdit LSP Trace');
	context.subscriptions.push(traceChannel);

	logLine('Extension activated.');
	logLine(`  lsp.enabled      = ${cfg('lsp').get<boolean>('enabled')}`);
	logLine(`  lsp.devMode      = ${cfg('lsp.devMode').get<boolean>('enabled', false)}`);
	logLine(`  lsp.executable   = ${cfg('lsp').get<string>('executable') ?? '(not set)'}`);
	logLine(`  lsp.projectPath  = ${cfg('lsp.devMode').get<string>('projectPath') ?? '(not set)'}`);

	const restartServer = async () => {
		await stopLspClient();
		if (cfg('lsp').get<boolean>('enabled') === true && validateConfiguration()) {
			await startLspClient(context);
		}
	};

	context.subscriptions.push(
		vscode.workspace.onDidChangeConfiguration(async e => {
			if (!e.affectsConfiguration('aet-eaw-edit')) { return; }

			const enabled = cfg('lsp').get<boolean>('enabled') === true;
			logLine(`Configuration changed. lsp.enabled=${enabled}, server running=${!!lspClient}`);

			if (!enabled && lspClient) {
				await stopLspClient();
			} else if (enabled && !lspClient && validateConfiguration()) {
				logLine('Starting LSP server after configuration change.');
				await startLspClient(context);
			} else if (lspClient && e.affectsConfiguration('aet-eaw-edit.features')) {
				// Feature flags travel once via initializationOptions - restart to apply them.
				logLine('Feature flags changed - restarting LSP server.');
				await restartServer();
			} else {
				validateConfiguration();
			}
		})
	);

	context.subscriptions.push(
		vscode.commands.registerCommand('aet-eaw-edit.lsp.debug.forceStartup', async () => {
			await stopLspClient();
			const devMode = cfg('lsp.devMode').get<boolean>('enabled', false);
			if (devMode || cfg('lsp').get<string>('executable')) {
				await startLspClient(context);
			} else {
				vscode.window.showErrorMessage(
					'EaWEdit: No LSP executable configured. Set "aet-eaw-edit.lsp.executable".'
				);
			}
		}),
		vscode.commands.registerCommand('aet-eaw-edit.lsp.restartServer', restartServer),
	);

	context.subscriptions.push(
		vscode.commands.registerCommand('aet-eaw-edit.lsp.revalidateWorkspace', async () => {
			await lsp.executeCommand('aet-eaw-edit.lsp.revalidateWorkspace', [],
				'could not revalidate the workspace');
		}),
	);

	context.subscriptions.push(
		vscode.commands.registerCommand('aet-eaw-edit.lsp.newModProject', async () => {
			if (!lsp.requireRunning()) { return; }
			const name = await vscode.window.showInputBox({
				prompt: 'Mod name',
				placeHolder: 'My Awesome Mod',
				validateInput: v => v?.trim() ? null : 'Name is required',
			});
			if (!name) {return;}
			const folders = await vscode.window.showOpenDialog({
				canSelectFolders: true, canSelectFiles: false, canSelectMany: false,
				openLabel: 'Select mod root folder',
			});
			if (!folders?.length) {return;}
			// Announced only once it actually ran. The old code said "created" whether or not the
			// request got through, so a server that had died reported a project that did not exist.
			const created = await lsp.executeCommand(
				'aet-eaw-edit.lsp.newModProject',
				[{ name: name.trim(), path: folders[0].fsPath }],
				`could not create the mod project '${name.trim()}'`);
			if (created) {
				vscode.window.showInformationMessage(`Mod project '${name.trim()}' created.`);
			}
		}),
	);

	context.subscriptions.push(
		vscode.commands.registerCommand('aet-eaw-edit.lsp.reloadProject', async () => {
			await lsp.executeCommand('aet-eaw-edit.lsp.reloadProject', [],
				'could not reload the project');
		}),
	);

	context.subscriptions.push(
		vscode.commands.registerCommand('aet-eaw-edit.lsp.initLocalisationProject', async () => {
			if (!lsp.requireRunning()) { return; }

			const config = await lsp.request<GetRootLocalisationConfigResult>(
				'aet/getRootLocalisationConfig');
			if (!config.ok) {
				vscode.window.showWarningMessage(
					'EaWEdit LSP: Could not query the project\'s localisation config.');
				return;
			}
			const rootConfig = config.value;

			// The .pgproj already declares a localisation node - it wins outright, no picker.
			if (rootConfig.configured) {
				const confirmed = await vscode.window.showInformationMessage(
					`Initialise the localisation project (${rootConfig.type} in "${rootConfig.directory}")?`,
					{ modal: true }, 'Initialise'
				);
				if (confirmed !== 'Initialise') { return; }
				await lsp.executeCommand('aet-eaw-edit.lsp.initLocalisationProject', [{}],
					'could not initialise the localisation project');
				return;
			}

			// Not configured - the VS Code setting only pre-fills the picker here, as a last resort.
			const formatOptions = [
				{ label: 'CSV', description: 'Comma-separated values (.csv)' },
				{ label: 'XML', description: 'eaw-translation v1 XML (.xml)' },
				{ label: 'NLS', description: 'Java-style properties (.properties)' },
			];
			const defaultFormat = cfg('localisation').get<string>('format', 'format-dat')
				.replace(/^format-/, '').toUpperCase();
			const defaultIdx = formatOptions.findIndex(o => o.label === defaultFormat);
			if (defaultIdx > 0) { formatOptions.unshift(formatOptions.splice(defaultIdx, 1)[0]); }

			const formatItem = await vscode.window.showQuickPick(formatOptions, {
				title: 'Initialise Localisation Project from Baseline', placeHolder: 'Select output format'
			});
			if (!formatItem) { return; }

			const directory = await vscode.window.showInputBox({
				title: 'Localisation directory (relative to the .pgproj)',
				value: 'data/text',
				prompt: 'Where the localisation project files will live',
				validateInput: v => (v?.trim() ? null : 'A directory is required'),
			});
			if (!directory) { return; }

			await lsp.executeCommand(
				'aet-eaw-edit.lsp.initLocalisationProject',
				[{ format: formatItem.label, directory: directory.trim() }],
				'could not initialise the localisation project');
		}),
	);

	context.subscriptions.push(
		vscode.commands.registerCommand('aet-eaw-edit.lsp.importLocalisationProject', async () => {
			if (!lsp.requireRunning()) { return; }

			const convertibleFormats = [
				{ label: 'CSV', description: 'Comma-separated values (.csv)' },
				{ label: 'XML', description: 'eaw-translation v1 XML (.xml)' },
				{ label: 'NLS', description: 'Java-style properties (.properties)' },
			];
			const datFormat = { label: 'DAT', description: 'The game\'s proprietary binary format (.dat)' };
			const sourceFormatChoices = [...convertibleFormats, datFormat];

			const sourceFormatItem = await vscode.window.showQuickPick(sourceFormatChoices, {
				title: 'Import Existing Localisation Files', placeHolder: 'What format are your existing files in?'
			});
			if (!sourceFormatItem) { return; }
			const isDatSource = sourceFormatItem.label === 'DAT';

			// DAT is one binary file per language (MasterTextFile_<LANGUAGE>.dat) - the user picks
			// one file, the server discovers its siblings in the same folder. Every other format
			// stores all languages in one file, so the user picks the containing folder directly.
			let sourceDirectory: string;
			const defaultUri = vscode.workspace.workspaceFolders?.[0]?.uri;
			if (isDatSource) {
				const sourceFiles = await vscode.window.showOpenDialog({
					canSelectFolders: false, canSelectFiles: true, canSelectMany: false,
					defaultUri,
					filters: { 'DAT files': ['dat'] },
					openLabel: 'Select a MasterTextFile_<LANGUAGE>.dat file',
					title: 'Select one DAT file - its siblings in the same folder are imported too',
				});
				if (!sourceFiles?.length) { return; }
				sourceDirectory = vscode.Uri.joinPath(sourceFiles[0], '..').fsPath;
			} else {
				const sourceFolders = await vscode.window.showOpenDialog({
					canSelectFolders: true, canSelectFiles: false, canSelectMany: false,
					defaultUri,
					openLabel: 'Select folder with existing files',
					title: `Folder containing your ${sourceFormatItem.label} files`,
				});
				if (!sourceFolders?.length) { return; }
				sourceDirectory = sourceFolders[0].fsPath;
			}

			// Pre-fill the target format to match the source - the common case is "just register
			// what I already have," not a conversion. DAT is never a conversion target (no DAT
			// generator wired into this wizard - use the existing "Export DAT" action for that), so
			// it only appears as a target choice when the source is DAT itself, with no pre-selection
			// (DAT is never git-friendly for collaborative editing - force an explicit choice).
			let targetFormatItem: { label: string; description: string } | undefined;
			if (isDatSource) {
				targetFormatItem = await vscode.window.showQuickPick([...convertibleFormats, datFormat], {
					title: 'Target Format', placeHolder: 'Format to use going forward'
				});
			} else {
				const targetChoices = convertibleFormats.map(o => ({ ...o }));
				const sourceIdx = targetChoices.findIndex(o => o.label === sourceFormatItem.label);
				if (sourceIdx > 0) { targetChoices.unshift(targetChoices.splice(sourceIdx, 1)[0]); }
				targetFormatItem = await vscode.window.showQuickPick(targetChoices, {
					title: 'Target Format',
					placeHolder: `Format to use going forward (defaults to ${sourceFormatItem.label})`
				});
			}
			if (!targetFormatItem) { return; }

			let targetDirectory: string | undefined;
			if (targetFormatItem.label !== sourceFormatItem.label) {
				targetDirectory = await vscode.window.showInputBox({
					title: 'Target directory (relative to the .pgproj)',
					value: 'data/text',
					prompt: `Converting ${sourceFormatItem.label} to ${targetFormatItem.label} - where should the new file go?`,
					validateInput: v => (v?.trim() ? null : 'A directory is required'),
				});
				if (!targetDirectory) { return; }
			}

			await lsp.executeCommand(
				'aet-eaw-edit.lsp.importLocalisationProject',
				[{
					sourceFormat: sourceFormatItem.label,
					sourceDirectory,
					targetFormat: targetFormatItem.label,
					...(targetDirectory ? { targetDirectory: targetDirectory.trim() } : {}),
				}],
				'could not import the localisation files');
			// No message here: executeCommand returns nothing to check, so anything said at this
			// point is a guess. The server reports the outcome - what it wrote, or why it did not.
		}),
	);

	context.subscriptions.push(
		vscode.commands.registerCommand('aet-eaw-edit.lsp.showReferences',
			(uriStr: string,
			 position: { line: number; character: number },
			 rawLocations: { uri: string; range: { start: { line: number; character: number }; end: { line: number; character: number } } }[]) => {
				const uri = vscode.Uri.parse(uriStr);
				const pos = new vscode.Position(position.line, position.character);
				const locations = rawLocations.map(l =>
					new vscode.Location(
						vscode.Uri.parse(l.uri),
						new vscode.Range(
							new vscode.Position(l.range.start.line, l.range.start.character),
							new vscode.Position(l.range.end.line, l.range.end.character)
						)
					)
				);
				vscode.commands.executeCommand('editor.action.showReferences', uri, pos, locations);
			})
	);

	// Opens the encyclopedia popup preview for a GameObject. Triggered by the "preview encyclopedia"
	// code lens (objectId passed as the first argument), or from the command palette (prompts).
	// Opening a .alo or .ala shows the model instead of the binary editor. Registered whatever the
	// feature flag says: the flag gates the SERVER endpoints, and a custom editor that vanishes with
	// a setting would leave those files opening as binary with no hint why.
	context.subscriptions.push(ModelPreviewEditorProvider.register(context, lsp));

	context.subscriptions.push(
		vscode.commands.registerCommand('aet-eaw-edit.lsp.previewModel',
			async (referenceArg?: string) => {
				if (!lsp.requireRunning()) { return; }

				let reference = referenceArg;
				if (!reference) {
					reference = await vscode.window.showInputBox({
						title: 'Preview Model',
						prompt: 'Model file name, e.g. EV_StarDestroyer.ALO',
						validateInput: v => (v?.trim() ? null : 'Model name is required'),
					});
				}
				if (!reference?.trim()) { return; }

				const name = reference.trim();
				ModelPreviewPanel.show(context.extensionUri, lsp,
					{ kind: 'model', modelReference: name }, name);
			}));

	// Never wired to activation: a modder who has not asked for shader-accurate previews should not
	// be nagged about a download they may not want.
	context.subscriptions.push(
		vscode.commands.registerCommand('aet-eaw-edit.shaders.obtain',
			async () => { await offerShaderSources(context); }));

	context.subscriptions.push(
		vscode.commands.registerCommand('aet-eaw-edit.lsp.previewAssembledUnit',
			async (objectIdArg?: string) => {
				if (!lsp.requireRunning()) { return; }

				let objectId = objectIdArg;
				if (!objectId) {
					objectId = await vscode.window.showInputBox({
						title: 'Preview Assembled Unit',
						prompt: 'Name of the GameObject to assemble, e.g. Generic_Star_Destroyer',
						validateInput: v => (v?.trim() ? null : 'Object name is required'),
					});
				}
				if (!objectId?.trim()) { return; }

				const id = objectId.trim();
				ModelPreviewPanel.show(context.extensionUri, lsp,
					{ kind: 'object', objectId: id }, id);
			}));

	context.subscriptions.push(
		vscode.commands.registerCommand('aet-eaw-edit.lsp.showEncyclopedia', async (objectIdArg?: string) => {
			if (!lsp.requireRunning()) { return; }

			let objectId = objectIdArg;
			if (!objectId) {
				objectId = await vscode.window.showInputBox({
					title: 'Preview Encyclopedia Popup',
					prompt: 'Name of the GameObject whose popup to preview',
					validateInput: v => (v?.trim() ? null : 'Object name is required'),
				});
			}
			if (!objectId?.trim()) { return; }
			const id = objectId.trim();

			// Both the first open and the SP/MP toggle go through here, so the panel never has to
			// know how to reach the server - it asks, this fetches.
			const load = async (multiplayer: boolean, retarget: boolean): Promise<void> => {
				const entry = await lsp.requestOrReport<GetEncyclopediaEntryResult>(
					'aet/getEncyclopediaEntry', { objectId: id, multiplayer },
					'load the encyclopedia entry');
				if (entry === undefined) { return; }

				if (retarget) {
					EncyclopediaPanel.show(context.extensionUri, entry,
						mp => void load(mp, false));
				} else {
					EncyclopediaPanel.update(entry);
				}
			};

			// Retargeting keeps whatever SP/MP mode the panel is already showing.
			await load(EncyclopediaPanel.multiplayer(), true);
		})
	);

	// Opens the read-only "effective object" virtual document for a variant GameObject. Triggered by
	// the "show effective object" code lens (objectId passed as the first argument), or from the command
	// palette (prompts for the object name).
	context.subscriptions.push(
		vscode.commands.registerCommand('aet-eaw-edit.lsp.showEffectiveObject', async (objectIdArg?: string) => {
			if (!lsp.requireRunning()) { return; }

			let objectId = objectIdArg;
			if (!objectId) {
				objectId = await vscode.window.showInputBox({
					title: 'Show Effective Object',
					prompt: 'Name of the variant GameObject to resolve',
					validateInput: v => (v?.trim() ? null : 'Object name is required'),
				});
			}
			if (!objectId?.trim()) { return; }
			objectId = objectId.trim();

			// The tab label is derived from the URI's path basename, so bake the intent into it: this is a
			// generated, read-only preview of the fully-merged object, not an editable source file. The real
			// object id travels in the query, so the decorative basename doesn't affect content resolution.
			const uri = vscode.Uri.from({
				scheme: EFFECTIVE_SCHEME,
				path: `/${objectId} (effective preview, read-only).xml`,
				query: objectId,
			});
			effectiveObjectProvider?.refresh(uri);
			const doc = await vscode.workspace.openTextDocument(uri);
			// Open beside the active editor so the preview sits next to the source it was generated from.
			await vscode.window.showTextDocument(doc, {
				preview: true,
				viewColumn: vscode.ViewColumn.Beside,
			});
		})
	);

	if (cfg('lsp').get<boolean>('enabled') === true && validateConfiguration()) {
		logLine('LSP enabled - starting server.');
		void startLspClient(context);
	} else if (cfg('lsp').get<boolean>('enabled') !== true) {
		logLine('LSP disabled (aet-eaw-edit.lsp.enabled is false). Use the restart command or enable it in settings.');
	}
}

export async function deactivate(): Promise<void> {
	logLine('Extension deactivated.');
	statusItem?.hide();
	StoryGraphPanel.disposeAll();
	LocalisationEditorPanel.disposeAll();
	CreditsPreviewPanel.disposeAll();
	EncyclopediaPanel.disposeAll();
	ModelPreviewPanel.disposeAll();
	ModelInspectorPanel.disposeAll();
	await stopLspClient();
}
