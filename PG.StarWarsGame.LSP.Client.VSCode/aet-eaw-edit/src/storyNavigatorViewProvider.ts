// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as vscode from 'vscode';

import { LspGateway } from './lsp/lspGateway';
import { LspTreeDataProvider } from './lspTreeDataProvider';
import {
    GetStoryPlotsResult, StoryCampaignDto, StoryFactionDto, StoryLuaScriptDto, StoryPlotThreadDto,
} from './protocol';

type StoryNodeKind = 'set' | 'campaign' | 'faction' | 'thread' | 'lua' | 'info';

export class StoryTreeItem extends vscode.TreeItem {
    constructor(
        label: string,
        collapsibleState: vscode.TreeItemCollapsibleState,
        public readonly kind: StoryNodeKind,
        public readonly campaignName?: string,
        public readonly factionName?: string,
        public readonly fileName?: string,
        // The Campaign_Set value a 'set' node represents; undefined on the "Ungrouped" set node.
        public readonly setName?: string
    ) {
        super(label, collapsibleState);
    }
}

/**
 * Campaign navigator: set (Campaign_Set) → campaign → faction (plot manifest) → story threads +
 * attached Lua scripts, fed by `aet/getStoryPlots`. Campaigns are always grouped by their
 * Campaign_Set; every set is shown (even a single-campaign set), and campaigns that declare no
 * Campaign_Set fall under an "Ungrouped" node. The tree re-fetches on every expand of the root, so
 * a plain `refresh()` after `aet/storyGraphChanged` is enough to stay current.
 */
export class StoryNavigatorViewProvider
    extends LspTreeDataProvider<StoryTreeItem, GetStoryPlotsResult> {
    public static readonly viewId = 'aet-eaw-edit.lsp.storyNavigator';

    protected readonly method = 'aet/getStoryPlots';
    protected readonly subject = 'story campaigns';

    constructor(lsp: LspGateway) {
        super(lsp);
    }

    /**
     * The campaigns the tree is currently built from.
     *
     * Every level below the root is drilled out of this rather than re-fetched, so an expand costs
     * nothing. Empty when the root has not loaded, which is the only state in which a child can be
     * asked for before the data exists.
     */
    private get _campaigns(): StoryCampaignDto[] {
        return this.data?.campaigns ?? [];
    }

    protected errorOf(data: GetStoryPlotsResult): string | null | undefined {
        return data.error;
    }

    protected isEmpty(data: GetStoryPlotsResult): boolean {
        return !data.campaigns || data.campaigns.length === 0;
    }

    protected rootItems(): StoryTreeItem[] {
        return this._setItems();
    }

    protected infoItem(message: string): StoryTreeItem {
        return this._infoItem(message);
    }

    protected failureMessage(
        outcome: { reason: 'offline' | 'failed'; message: string },
    ): string {
        return outcome.reason === 'offline'
            ? 'LSP server is not running.'
            : `Cannot load story plots: ${outcome.message}`;
    }

    protected childrenOf(element: StoryTreeItem): StoryTreeItem[] {
        if (element.kind === 'set') {
            // A named set lists the campaigns carrying that Campaign_Set; the "Ungrouped" node
            // (setName undefined) lists the campaigns that declare none.
            const inSet = this._campaigns.filter(c =>
                element.setName !== undefined ? c.set === element.setName : !c.set);
            return inSet.map(c => this._campaignItem(c));
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

    // Root level: one node per Campaign_Set (sorted), then an "Ungrouped" node for campaigns with
    // no set. Every set is shown even when it holds a single campaign.
    private _setItems(): StoryTreeItem[] {
        const ungrouped: StoryCampaignDto[] = [];
        const bySet = new Map<string, StoryCampaignDto[]>();
        for (const c of this._campaigns) {
            if (!c.set) { ungrouped.push(c); continue; }
            const list = bySet.get(c.set);
            if (list) { list.push(c); } else { bySet.set(c.set, [c]); }
        }

        const items = [...bySet.keys()]
            .sort((a, b) => a.localeCompare(b))
            .map(setName => this._setItem(setName, bySet.get(setName)!.length));
        if (ungrouped.length) {
            items.push(this._setItem(undefined, ungrouped.length));
        }
        return items;
    }

    // setName undefined => the "Ungrouped" bucket.
    private _setItem(setName: string | undefined, count: number): StoryTreeItem {
        const label = setName ?? 'Ungrouped';
        const item = new StoryTreeItem(
            label, vscode.TreeItemCollapsibleState.Collapsed, 'set',
            undefined, undefined, undefined, setName);
        item.iconPath = new vscode.ThemeIcon(setName ? 'folder-library' : 'folder');
        item.description = count === 1 ? '1 campaign' : `${count} campaigns`;
        item.tooltip = setName
            ? `Campaign set "${setName}" - ${count} campaign(s)`
            : 'Campaigns with no Campaign_Set';
        item.contextValue = setName ? 'aetCampaignSet' : 'aetCampaignSetUngrouped';
        return item;
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
