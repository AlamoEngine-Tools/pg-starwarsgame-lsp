// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as vscode from 'vscode';
import { LanguageClient } from 'vscode-languageclient/node';

interface StoryPlotThreadDto { file: string; suspended: boolean; uri?: string | null; }
interface StoryLuaScriptDto { name: string; uri?: string | null; }
interface StoryFactionDto {
    faction: string; manifestFile: string; threads: StoryPlotThreadDto[]; luaScripts: StoryLuaScriptDto[];
}
interface StoryCampaignDto { name: string; factions: StoryFactionDto[]; }
interface GetStoryPlotsResult { campaigns: StoryCampaignDto[]; error?: string | null; }

type StoryNodeKind = 'campaign' | 'faction' | 'thread' | 'lua' | 'info';

export class StoryTreeItem extends vscode.TreeItem {
    constructor(
        label: string,
        collapsibleState: vscode.TreeItemCollapsibleState,
        public readonly kind: StoryNodeKind,
        public readonly campaignName?: string,
        public readonly factionName?: string,
        public readonly fileName?: string
    ) {
        super(label, collapsibleState);
    }
}

/**
 * Campaign navigator: campaign → faction (plot manifest) → story threads + attached Lua scripts,
 * fed by `aet/getStoryPlots`. The tree re-fetches on every expand of the root, so a plain
 * `refresh()` after `aet/storyGraphChanged` is enough to stay current.
 */
export class StoryNavigatorViewProvider implements vscode.TreeDataProvider<StoryTreeItem> {
    public static readonly viewId = 'aet-eaw-edit.lsp.storyNavigator';

    private readonly _onDidChangeTreeData = new vscode.EventEmitter<StoryTreeItem | undefined>();
    readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

    private _campaigns: StoryCampaignDto[] = [];

    constructor(private readonly _getLspClient: () => LanguageClient | undefined) {}

    refresh(): void {
        this._onDidChangeTreeData.fire(undefined);
    }

    getTreeItem(element: StoryTreeItem): vscode.TreeItem {
        return element;
    }

    async getChildren(element?: StoryTreeItem): Promise<StoryTreeItem[]> {
        if (!element) {
            return this._loadRoot();
        }
        if (element.kind === 'campaign') {
            const campaign = this._campaigns.find(c => c.name === element.campaignName);
            return (campaign?.factions ?? []).map(f => this._factionItem(campaign!.name, f));
        }
        if (element.kind === 'faction') {
            const faction = this._campaigns
                .find(c => c.name === element.campaignName)?.factions
                .find(f => f.faction === element.factionName);
            if (!faction) { return []; }
            return [
                ...faction.threads.map(t => this._threadItem(t)),
                ...faction.luaScripts.map(s => this._luaItem(s)),
            ];
        }
        return [];
    }

    private async _loadRoot(): Promise<StoryTreeItem[]> {
        const client = this._getLspClient();
        if (!client) {
            return [this._infoItem('LSP server is not running.')];
        }
        try {
            const result = await client.sendRequest<GetStoryPlotsResult>('aet/getStoryPlots', {});
            if (result.error) {
                return [this._infoItem(result.error)];
            }
            this._campaigns = result.campaigns ?? [];
        } catch (e) {
            return [this._infoItem(`Cannot load story plots: ${e}`)];
        }
        if (!this._campaigns.length) {
            return [this._infoItem('No story campaigns found in this workspace.')];
        }
        return this._campaigns.map(c => this._campaignItem(c));
    }

    private _campaignItem(campaign: StoryCampaignDto): StoryTreeItem {
        const item = new StoryTreeItem(
            campaign.name, vscode.TreeItemCollapsibleState.Collapsed, 'campaign', campaign.name);
        item.iconPath = new vscode.ThemeIcon('map');
        item.contextValue = 'aetStoryCampaign';
        item.tooltip = `${campaign.name} - click the graph icon to open the story graph`;
        return item;
    }

    private _factionItem(campaignName: string, faction: StoryFactionDto): StoryTreeItem {
        const item = new StoryTreeItem(
            faction.faction, vscode.TreeItemCollapsibleState.Collapsed, 'faction',
            campaignName, faction.faction);
        item.iconPath = new vscode.ThemeIcon('organization');
        item.description = faction.manifestFile;
        return item;
    }

    private _threadItem(thread: StoryPlotThreadDto): StoryTreeItem {
        const item = new StoryTreeItem(
            thread.file, vscode.TreeItemCollapsibleState.None, 'thread',
            undefined, undefined, thread.file);
        item.iconPath = new vscode.ThemeIcon(thread.suspended ? 'circle-slash' : 'type-hierarchy-sub');
        item.description = thread.suspended ? 'suspended' : undefined;
        item.tooltip = thread.suspended
            ? `${thread.file} - suspended until a STORY_ELEMENT reward activates it`
            : thread.file;
        item.command = {
            command: 'aet-eaw-edit.lsp.openStoryFile',
            title: 'Open Story File',
            arguments: [thread.file, thread.uri ?? undefined],
        };
        return item;
    }

    private _luaItem(script: StoryLuaScriptDto): StoryTreeItem {
        const item = new StoryTreeItem(
            script.name, vscode.TreeItemCollapsibleState.None, 'lua',
            undefined, undefined, script.name);
        item.iconPath = new vscode.ThemeIcon('file-code');
        item.description = 'Lua';
        item.command = {
            command: 'aet-eaw-edit.lsp.openStoryFile',
            title: 'Open Story Script',
            arguments: [
                script.name.toLowerCase().endsWith('.lua') ? script.name : `${script.name}.lua`,
                script.uri ?? undefined,
            ],
        };
        return item;
    }

    private _infoItem(message: string): StoryTreeItem {
        const item = new StoryTreeItem(
            message, vscode.TreeItemCollapsibleState.None, 'info');
        item.iconPath = new vscode.ThemeIcon('info');
        return item;
    }
}
