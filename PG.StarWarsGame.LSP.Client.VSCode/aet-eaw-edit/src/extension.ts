// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

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

interface LocProjectInfo { label: string; filePath: string; resourceType: string; }
interface GetLocalisationProjectsResult { projects: LocProjectInfo[]; }

let lspClient: LanguageClient | undefined;
let localisationEditorProvider: LocalisationEditorViewProvider | undefined;
let statusItem: vscode.StatusBarItem | undefined;
let traceChannel: vscode.OutputChannel | undefined;
let log: vscode.OutputChannel | undefined;

function logLine(msg: string): void {
	const ts = new Date().toISOString().replace('T', ' ').replace('Z', '');
	log?.appendLine(`[${ts}] ${msg}`);
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
		if (!cfg('lsp').get<string>('executable')) {
			vscode.window.showErrorMessage(
				'EaWEdit: LSP is enabled but no server executable is configured. ' +
				'Set "aet-eaw-edit.lsp.executable" to the path of the LSP server DLL.'
			);
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
			baselineLocalPath: cfg('lsp.source.baseline').get<string>('localPath') || undefined,
			modPaths:          cfg('lsp').get<string[]>('modPaths', []),
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
					projects.map(p => ({ label: p.label, description: p.filePath, detail: p.resourceType, filePath: p.filePath })),
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
		vscode.window.showErrorMessage(
			`EaWEdit: LSP server failed to start. Check that the executable path is correct. (${e})`
		);
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

	statusItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 0);
	statusItem.tooltip = CLIENT_NAME;
	context.subscriptions.push(statusItem);

	log = vscode.window.createOutputChannel('EaWEdit');
	context.subscriptions.push(log);

	traceChannel = vscode.window.createOutputChannel('EaWEdit LSP Trace');
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
			const formatItem = await vscode.window.showQuickPick(
				[
					{ label: 'CSV', description: 'Comma-separated values (.csv)' },
					{ label: 'XML', description: 'eaw-translation v1 XML (.xml)' },
					{ label: 'NLS', description: 'Java-style properties (.properties)' },
				],
				{ title: 'Initialise Localisation Project from Baseline', placeHolder: 'Select output format' }
			);
			if (!formatItem) { return; }
			await lspClient.sendRequest(ExecuteCommandRequest.type, {
				command: 'aet-eaw-edit.lsp.initLocalisationProject',
				arguments: [{ format: formatItem.label }],
			});
			vscode.window.showInformationMessage(`Localisation project initialised (${formatItem.label}).`);
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
