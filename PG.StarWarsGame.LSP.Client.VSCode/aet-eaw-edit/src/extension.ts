// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import {
	ClientCapabilities,
	ExecuteCommandRequest,
	FeatureState,
	LanguageClient,
	LanguageClientOptions,
	ServerOptions,
	StaticFeature,
	Trace,
	TransportKind,
} from 'vscode-languageclient/node';
import { LocalisationEditorViewProvider } from './localisationEditorViewProvider';

const CLIENT_ID = 'aet.pg.swg.lsp';
const CLIENT_NAME = 'Alamo Engine Tools - Empire at War Edit';
const RELEASES_URL = 'https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/releases';
const REQUIRED_SERVER_VERSION = '0.1.2';

/**
 * Forces every language-feature capability to be advertised STATICALLY (in the initialize response)
 * rather than via dynamic `registerCapability`.
 * 
 * Reporting `dynamicRegistration = false` for each capability makes the server emit it in the static
 * initialize response instead. Static capabilities are wired up deterministically at connect time,
 * identically on every run, immune to the batch-abort. `executeCommand` is intentionally left dynamic
 * (the extension owns those commands and forwards them via direct `sendRequest`, so its registration
 * failing is harmless).
 */
class ForceStaticCapabilitiesFeature implements StaticFeature {
	// Every textDocument client-capability that exposes a `dynamicRegistration` flag and that this
	// server actually provides. `synchronization` MUST be included — it is what replays `didOpen`.
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

interface LocProjectInfo {
	label: string; filePath: string; resourceType: string; projectName: string; rank: number;
}
interface GetLocalisationProjectsResult { projects: LocProjectInfo[]; }

interface GetEffectiveObjectResult {
	found: boolean;
	cyclic: boolean;
	cycleObjectId?: string;
	chain: string[];
	xml: string;
	typeName?: string;
}

/** URI scheme for the read-only "effective object" virtual documents (variant inheritance). */
const EFFECTIVE_SCHEME = 'aet-effective';

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
		if (!lspClient) {
			return '<!-- EaWEdit: LSP server is not running. -->';
		}
		try {
			const result = await lspClient.sendRequest<GetEffectiveObjectResult>(
				'aet/getEffectiveObject', { objectId });
			if (!result.found) {
				return `<!-- EaWEdit: no object named '${objectId}' was found in the workspace. -->`;
			}
			return result.xml;
		} catch (e) {
			return `<!-- EaWEdit: failed to resolve effective object '${objectId}': ${e} -->`;
		}
	}
}

let lspClient: LanguageClient | undefined;
let effectiveObjectProvider: EffectiveObjectContentProvider | undefined;
let localisationEditorProvider: LocalisationEditorViewProvider | undefined;
let statusItem: vscode.StatusBarItem | undefined;
let traceChannel: vscode.LogOutputChannel | undefined;
let log: vscode.OutputChannel | undefined;

function logLine(msg: string): void {
	const ts = new Date().toISOString().replace('T', ' ').replace('Z', '');
	log?.appendLine(`[${ts}] ${msg}`);
}

/**
 * vscode-languageclient v10 types `traceOutputChannel` as `LogOutputChannel` and writes protocol
 * trace via its `trace()` method — which a real `LogOutputChannel` hides unless its (user-controlled)
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
		logLine(`Dev mode: starting from source — ${serverExe} ${serverArgs.join(' ')}`);
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

	const clientOptions: LanguageClientOptions = {
		documentSelector: [
			{ scheme: 'file', language: 'xml' },
			{ scheme: 'file', language: 'lua' },
		],
		traceOutputChannel: traceChannel,
		initializationOptions: {
			workspaceRoot:     vscode.workspace.workspaceFolders?.[0]?.uri.fsPath,
			baseGamePath:      cfg('lsp.source').get<string>('baseGameDirectory') || undefined,
			expansionGamePath: cfg('lsp.source').get<string>('expansionDirectory') || undefined,
			locale:            cfg('lsp').get<string>('locale', 'en'),
			schemaUrl:         schemaSource === 'http' ? (cfg('lsp.schema').get<string>('url') || undefined) : undefined,
			schemaLocalPath:   schemaSource === 'local' ? (cfg('lsp.schema').get<string>('localPath') || undefined) : undefined,
			baselineType:      cfg('lsp.source.baseline').get<string>('type', 'http'),
			baselineUrl:       cfg('lsp.source.baseline').get<string>('type', 'http') === 'http'
			                       ? (cfg('lsp.source.baseline').get<string>('url') || undefined)
			                       : undefined,
			baselineLocalPath: cfg('lsp.source.baseline').get<string>('localPath') || undefined
		},
		middleware: {
			executeCommand: async (command, args, next) => {
				if (command !== 'aet-eaw-edit.lsp.createLocalisationKey') {
					return next(command, args);
				}

				const keyName = (args[0] as string | undefined) ?? '';
				if (!keyName) {
					vscode.window.showWarningMessage('EaWEdit: no localisation key name provided.');
					return;
				}

				let projects: LocProjectInfo[] = [];
				try {
					const result = await lspClient!.sendRequest<GetLocalisationProjectsResult>(
						'aet/getLocalisationProjects', {});
					projects = result.projects ?? [];
				} catch {
					vscode.window.showWarningMessage('EaWEdit: could not fetch localisation projects from server.');
					return;
				}

				if (!projects.length) {
					vscode.window.showWarningMessage(
						"EaWEdit: no localisation projects found. Use 'EaWEdit: Initialise Localisation Project from Baseline' first.");
					return;
				}

				const picked = await vscode.window.showQuickPick(
					projects.map(p => ({
						label: p.label, description: p.filePath,
						detail: `${p.projectName} · ${p.resourceType}`, filePath: p.filePath
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
		statusItem.text = '$(loading~spin) EaWEdit LSP: starting…';
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

		const serverVersion = lspClient?.initializeResult?.serverInfo?.version;
		logLine(`Server version reported: ${serverVersion ?? '(none)'}`);
		if (serverVersion !== REQUIRED_SERVER_VERSION) {
			const msg = serverVersion
				? `EaWEdit: server version ${serverVersion} does not match the expected version ${REQUIRED_SERVER_VERSION}. Some features may not work correctly.`
				: `EaWEdit: the server did not report a version. Download version ${REQUIRED_SERVER_VERSION} from the releases page.`;
			void vscode.window.showWarningMessage(msg, 'Download').then(choice => {
				if (choice === 'Download') {
					void vscode.env.openExternal(vscode.Uri.parse(RELEASES_URL));
				}
			});
		}

		// vscode-languageclient's hookConfigurationChanged reads aet.pg.swg.lsp.trace.server
		// (the CLIENT_ID namespace), which is not in settings, so it resets trace to Off on
		// every VS Code config change. Register our own listener — after hookConfigurationChanged's
		// listener, so it fires last and wins — to re-apply the user's actual setting.
		context.subscriptions.push(
			vscode.workspace.onDidChangeConfiguration(() => {
				const level = cfg('lsp.debug').get<string>('traceServer', 'off');
				void lspClient?.setTrace(traceMap[level] ?? Trace.Off);
			})
		);
	}).catch((e: unknown) => {
		logLine(`LSP server failed to start: ${e}`);
		lspClient = undefined;
		if (statusItem) {
			statusItem.text = '$(error) EaWEdit LSP: failed to start';
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
	});

	lspClient.onNotification('aet/localisationIndexUpdated', async () => {
		logLine('Localisation index updated — refreshing editor panel.');
		await localisationEditorProvider?.refresh();
	});
}

async function stopLspClient(): Promise<void> {
	if (lspClient) {
		logLine('Stopping LSP server.');
		await lspClient.stop();
		lspClient = undefined;
	}
}

export async function activate(context: vscode.ExtensionContext): Promise<void> {

	localisationEditorProvider = new LocalisationEditorViewProvider(context.extensionUri, () => lspClient);
	context.subscriptions.push(
		vscode.window.registerWebviewViewProvider(
			LocalisationEditorViewProvider.viewId,
			localisationEditorProvider
		)
	);

	effectiveObjectProvider = new EffectiveObjectContentProvider();
	context.subscriptions.push(
		vscode.workspace.registerTextDocumentContentProvider(EFFECTIVE_SCHEME, effectiveObjectProvider)
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
			} else {
				validateConfiguration();
			}
		})
	);

	const restartServer = async () => {
		await stopLspClient();
		if (cfg('lsp').get<boolean>('enabled') === true && validateConfiguration()) {
			await startLspClient(context);
		}
	};

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
			if (!lspClient) {
				vscode.window.showWarningMessage('EaWEdit LSP: server is not running.');
				return;
			}
			await lspClient.sendRequest(ExecuteCommandRequest.type, {
				command: 'aet-eaw-edit.lsp.revalidateWorkspace',
				arguments: [],
			});
		}),
	);

	context.subscriptions.push(
		vscode.commands.registerCommand('aet-eaw-edit.lsp.newModProject', async () => {
			if (!lspClient) { vscode.window.showWarningMessage('EaWEdit LSP: server is not running.'); return; }
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
			await lspClient.sendRequest(ExecuteCommandRequest.type, {
				command: 'aet-eaw-edit.lsp.newModProject',
				arguments: [{ name: name.trim(), path: folders[0].fsPath }],
			});
			vscode.window.showInformationMessage(`Mod project '${name.trim()}' created.`);
		}),
	);

	context.subscriptions.push(
		vscode.commands.registerCommand('aet-eaw-edit.lsp.reloadProject', async () => {
			if (!lspClient) { vscode.window.showWarningMessage('EaWEdit LSP: server is not running.'); return; }
			await lspClient.sendRequest(ExecuteCommandRequest.type, {
				command: 'aet-eaw-edit.lsp.reloadProject',
				arguments: [],
			});
		}),
	);

	context.subscriptions.push(
		vscode.commands.registerCommand('aet-eaw-edit.lsp.initLocalisationProject', async () => {
			if (!lspClient) { vscode.window.showWarningMessage('EaWEdit LSP: server is not running.'); return; }

			let rootConfig: { configured: boolean; type: string | null; directory: string | null };
			try {
				rootConfig = await lspClient.sendRequest('aet/getRootLocalisationConfig', {});
			} catch {
				vscode.window.showWarningMessage('EaWEdit LSP: could not query the project\'s localisation config.');
				return;
			}

			// The .pgproj already declares a localisation node — it wins outright, no picker.
			if (rootConfig.configured) {
				const confirmed = await vscode.window.showInformationMessage(
					`Initialise the localisation project (${rootConfig.type} in "${rootConfig.directory}")?`,
					{ modal: true }, 'Initialise'
				);
				if (confirmed !== 'Initialise') { return; }
				await lspClient.sendRequest(ExecuteCommandRequest.type, {
					command: 'aet-eaw-edit.lsp.initLocalisationProject',
					arguments: [{}],
				});
				vscode.window.showInformationMessage('Localisation project initialised.');
				return;
			}

			// Not configured — the VS Code setting only pre-fills the picker here, as a last resort.
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

			await lspClient.sendRequest(ExecuteCommandRequest.type, {
				command: 'aet-eaw-edit.lsp.initLocalisationProject',
				arguments: [{ format: formatItem.label, directory: directory.trim() }],
			});
			vscode.window.showInformationMessage(`Localisation project initialised (${formatItem.label}).`);
		}),
	);

	context.subscriptions.push(
		vscode.commands.registerCommand('aet-eaw-edit.lsp.importLocalisationProject', async () => {
			if (!lspClient) { vscode.window.showWarningMessage('EaWEdit LSP: server is not running.'); return; }

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

			// DAT is one binary file per language (MasterTextFile_<LANGUAGE>.dat) — the user picks
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
					title: 'Select one DAT file — its siblings in the same folder are imported too',
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

			// Pre-fill the target format to match the source — the common case is "just register
			// what I already have," not a conversion. DAT is never a conversion target (no DAT
			// generator wired into this wizard — use the existing "Export DAT" action for that), so
			// it only appears as a target choice when the source is DAT itself, with no pre-selection
			// (DAT is never git-friendly for collaborative editing — force an explicit choice).
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
					prompt: `Converting ${sourceFormatItem.label} → ${targetFormatItem.label} — where should the new file go?`,
					validateInput: v => (v?.trim() ? null : 'A directory is required'),
				});
				if (!targetDirectory) { return; }
			}

			await lspClient.sendRequest(ExecuteCommandRequest.type, {
				command: 'aet-eaw-edit.lsp.importLocalisationProject',
				arguments: [{
					sourceFormat: sourceFormatItem.label,
					sourceDirectory,
					targetFormat: targetFormatItem.label,
					...(targetDirectory ? { targetDirectory: targetDirectory.trim() } : {}),
				}],
			});
			vscode.window.showInformationMessage(
				targetDirectory
					? `Localisation project imported and converted to ${targetFormatItem.label}.`
					: 'Localisation project imported.'
			);
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

	// Opens the read-only "effective object" virtual document for a variant GameObject. Triggered by
	// the "show effective object" code lens (objectId passed as the first argument), or from the command
	// palette (prompts for the object name).
	context.subscriptions.push(
		vscode.commands.registerCommand('aet-eaw-edit.lsp.showEffectiveObject', async (objectIdArg?: string) => {
			if (!lspClient) { vscode.window.showWarningMessage('EaWEdit LSP: server is not running.'); return; }

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

			const uri = vscode.Uri.from({ scheme: EFFECTIVE_SCHEME, path: `/${objectId}.xml`, query: objectId });
			effectiveObjectProvider?.refresh(uri);
			const doc = await vscode.workspace.openTextDocument(uri);
			await vscode.window.showTextDocument(doc, { preview: true });
		})
	);

	if (cfg('lsp').get<boolean>('enabled') === true && validateConfiguration()) {
		logLine('LSP enabled — starting server.');
		void startLspClient(context);
	} else if (cfg('lsp').get<boolean>('enabled') !== true) {
		logLine('LSP disabled (aet-eaw-edit.lsp.enabled is false). Use the restart command or enable it in settings.');
	}
}

export async function deactivate(): Promise<void> {
	logLine('Extension deactivated.');
	statusItem?.hide();
	await stopLspClient();
}
