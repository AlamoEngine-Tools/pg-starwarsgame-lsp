// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as vscode from 'vscode';

import type {LspGateway} from '../lsp/lspGateway';
import {buildModChain, collectSourceRoots, type LaunchLayersResult} from './launchLayers';
import {DEBUGGER_DISABLED_MESSAGE, LUA_DEBUG_TYPE, luaDebuggerEnabled} from './luaDebugAdapterFactory';

/**
 * Fills in what a launch.json leaves out. The connection settings come from the `aet-eaw-edit.game`
 * settings, the script roots and the mod chain from the language server's view of the project
 * layers. A launch that cannot be assembled is refused with the reason rather than started
 * against the wrong files.
 */
export class LuaDebugConfigurationProvider implements vscode.DebugConfigurationProvider {
    constructor(private readonly _lsp: LspGateway) {
    }

    provideDebugConfigurations(): vscode.ProviderResult<vscode.DebugConfiguration[]> {
        return [
            {
                type: LUA_DEBUG_TYPE,
                request: 'attach',
                name: 'Attach to Empire at War (Lua)',
            },
            {
                type: LUA_DEBUG_TYPE,
                request: 'launch',
                name: 'Launch Empire at War (Lua)',
                program: '${config:aet-eaw-edit.game.executable}',
            },
        ];
    }

    async resolveDebugConfiguration(
        _folder: vscode.WorkspaceFolder | undefined,
        config: vscode.DebugConfiguration,
    ): Promise<vscode.DebugConfiguration | undefined | null> {
        if (!luaDebuggerEnabled()) {
            void vscode.window.showErrorMessage(DEBUGGER_DISABLED_MESSAGE);
            return undefined;
        }

        // F5 with no launch.json: attach, which needs nothing but a running game.
        if (!config.type && !config.request && !config.name) {
            config = {type: LUA_DEBUG_TYPE, request: 'attach', name: 'Attach to Empire at War (Lua)'};
        }

        const game = vscode.workspace.getConfiguration('aet-eaw-edit.game');
        config.host ??= game.get<string>('luaDebugHost', '127.0.0.1');
        config.port ??= game.get<number>('luaDebugPort', 1234);
        config.unsafeTableExpansion ??= game.get<boolean>('unsafeTableExpansion', false);

        const layers = await this._layers();

        if (config.sourceRoots === undefined) {
            config.sourceRoots = layers === undefined
                ? (vscode.workspace.workspaceFolders ?? []).map(f => f.uri.fsPath)
                : collectSourceRoots(layers.layers);
        }

        if (config.request === 'launch') {
            config.program ||= game.get<string>('executable', '');
            if (!config.program) {
                void vscode.window.showErrorMessage(
                    'No game executable is configured. Set "aet-eaw-edit.game.executable" or "program" in the launch configuration');
                return undefined;
            }

            const override = Array.isArray(config.modPaths) ? (config.modPaths as string[]) : undefined;
            if (override === undefined && layers === undefined) {
                void vscode.window.showErrorMessage(
                    'The language server is not running, so the mod chain cannot be built. Start it, or set "modPaths" in the launch configuration');
                return undefined;
            }

            const chain = buildModChain(layers?.layers ?? [], override);
            if (!chain.ok) {
                void vscode.window.showErrorMessage(chain.message);
                return undefined;
            }

            const userArgs = Array.isArray(config.args) ? (config.args as string[]) : [];
            config.args = [...game.get<string[]>('arguments', []), ...chain.args, ...userArgs];
        }

        return config;
    }

    private async _layers(): Promise<LaunchLayersResult | undefined> {
        if (!this._lsp.isRunning) {
            return undefined;
        }
        const outcome = await this._lsp.request<LaunchLayersResult>('aet/getLaunchLayers');
        if (!outcome.ok) {
            return undefined;
        }
        return outcome.value.enabled ? outcome.value : {enabled: false, layers: []};
    }
}
