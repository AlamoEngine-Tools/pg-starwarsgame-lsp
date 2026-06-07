// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as path from 'path';
import * as vscode from 'vscode';
import {
	ExecuteCommandRequest,
	LanguageClient,
	LanguageClientOptions,
	ServerOptions,
	Trace,
	TransportKind,
} from 'vscode-languageclient/node';

const CLIENT_ID = 'aet.pg.swg.lsp';
const CLIENT_NAME = 'Alamo Engine Tools - Empire at War Edit';

let lspClient: LanguageClient | undefined;
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
				'AET EaW Edit: LSP is enabled but no server executable is configured. ' +
				'Set "aet-eaw-edit.lsp.executable" to the path of the LSP server DLL.'
			);
			valid = false;
		}
	}

	if (cfg('lsp.source.baseline').get<string>('type') === 'local' &&
		!cfg('lsp.source.baseline').get<string>('localPath')) {
		vscode.window.showErrorMessage(
			'AET EaW Edit: Baseline type is "local" but no path is configured. ' +
			'Set "aet-eaw-edit.lsp.source.baseline.localPath".'
		);
		valid = false;
	}

	if (cfg('lsp.schema').get<string>('source') === 'local' &&
		!cfg('lsp.schema').get<string>('localPath')) {
		vscode.window.showErrorMessage(
			'AET EaW Edit: Schema source is "local" but no directory is configured. ' +
			'Set "aet-eaw-edit.lsp.schema.localPath".'
		);
		valid = false;
	}

	return valid;
}

async function startLspClient(context: vscode.ExtensionContext): Promise<void> {
	const serverExe = 'dotnet';
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
		serverArgs = waitForDebugger ? [serverPath, '--wait-for-debugger'] : [serverPath];
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
			xmlDirectories:    cfg('lsp').get<string[]>('xmlDirectories', []),
		},
	};

	lspClient = new LanguageClient(CLIENT_ID, CLIENT_NAME, serverOptions, clientOptions);

	if (statusItem) {
		statusItem.text = '$(loading~spin) AET EaW LSP: starting…';
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
			statusItem.text = '$(error) AET EaW LSP: failed to start';
		}
		vscode.window.showErrorMessage(
			`AET EaW Edit: LSP server failed to start. Check that the executable path is correct. (${e})`
		);
	});

	lspClient.onNotification('$/workspaceScanComplete', () => {
		logLine('Workspace scan complete.');
		if (statusItem) {
			statusItem.text = '$(check) AET EaW LSP';
		}
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

	statusItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 0);
	statusItem.tooltip = CLIENT_NAME;
	context.subscriptions.push(statusItem);

	log = vscode.window.createOutputChannel('AET EaW Edit');
	context.subscriptions.push(log);

	traceChannel = vscode.window.createOutputChannel('AET EaW LSP Trace');
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
					'AET EaW Edit: No LSP executable configured. Set "aet-eaw-edit.lsp.executable".'
				);
			}
		}),
		vscode.commands.registerCommand('aet-eaw-edit.lsp.restartServer', restartServer),
	);

	context.subscriptions.push(
		vscode.commands.registerCommand('aet-eaw-edit.lsp.revalidateWorkspace', async () => {
			if (!lspClient) {
				vscode.window.showWarningMessage('AET EaW LSP: server is not running.');
				return;
			}
			await lspClient.sendRequest(ExecuteCommandRequest.type, {
				command: 'aet-eaw-edit.lsp.revalidateWorkspace',
				arguments: [],
			});
		}),
	);

	context.subscriptions.push(
		vscode.commands.registerCommand('aet.newModProject', async () => {
			if (!lspClient) { vscode.window.showWarningMessage('AET EaW LSP: server is not running.'); return; }
			const name = await vscode.window.showInputBox({
				prompt: 'Mod name',
				placeHolder: 'My Awesome Mod',
				validateInput: v => v?.trim() ? null : 'Name is required',
			});
			if (!name) return;
			const folders = await vscode.window.showOpenDialog({
				canSelectFolders: true, canSelectFiles: false, canSelectMany: false,
				openLabel: 'Select mod root folder',
			});
			if (!folders?.length) return;
			await lspClient.sendRequest(ExecuteCommandRequest.type, {
				command: 'aet.newModProject',
				arguments: [{ name: name.trim(), path: folders[0].fsPath }],
			});
			vscode.window.showInformationMessage(`Mod project '${name.trim()}' created.`);
		}),
	);

	context.subscriptions.push(
		vscode.commands.registerCommand('aet.reloadProject', async () => {
			if (!lspClient) { vscode.window.showWarningMessage('AET EaW LSP: server is not running.'); return; }
			await lspClient.sendRequest(ExecuteCommandRequest.type, {
				command: 'aet.reloadProject',
				arguments: [],
			});
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
