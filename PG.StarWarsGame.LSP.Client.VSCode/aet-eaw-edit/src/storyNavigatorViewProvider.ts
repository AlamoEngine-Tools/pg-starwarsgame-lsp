// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as vscode from 'vscode';

import {LspGateway} from './lsp/lspGateway';
import {LspTreeDataProvider} from './lspTreeDataProvider';
import {
    GetStoryPlotsResult, StoryCampaignDto, StoryFactionDto, StoryLuaScriptDto, StoryPlotThreadDto,
} from './protocol';
import {type BattleBranch, factionTree} from './storyBattles';
import {
    definitionLabel, type FactionRole, factionRole, orderedDefinitions, orderedFactions, roleLabel, setSummary,
} from './storyNavigatorModel';

type StoryNodeKind = 'set' | 'campaign' | 'faction' | 'battle' | 'thread' | 'lua' | 'info';

export class StoryTreeItem extends vscode.TreeItem {
    /** The document the node's "Open File" button opens; undefined when there is none to open. */
    public openUri?: string;
    /** 0-based line to reveal in {@link openUri}. */
    public openLine?: number;

    constructor(
        label: string,
        collapsibleState: vscode.TreeItemCollapsibleState,
        public readonly kind: StoryNodeKind,
        public readonly campaignName?: string,
        public readonly factionName?: string,
        // A thread's or script's file; on a 'battle' node, the battle's key as the server names it.
        public readonly fileName?: string,
        // The Campaign_Set value a 'set' node represents; undefined on the "Ungrouped" set node.
        public readonly setName?: string
    ) {
        super(label, collapsibleState);
    }
}

const ROLE_ICONS: Record<FactionRole['kind'], string> = {
    player: 'person', human: 'account', ai: 'hubot', unset: 'organization',
};

/**
 * Campaign navigator: set (Campaign_Set) → campaign definition → faction (plot manifest) → galactic
 * story threads + the battles the story links out to (each over its own plot threads, in the order
 * the galactic story reaches them) + attached Lua scripts, fed by `aet/getStoryPlots`.
 *
 * A set is one campaign with a definition per playable faction, so its definitions are labelled by
 * the faction they are played as; campaigns declaring no Campaign_Set fall under "Ungrouped". No node
 * acts on a click: graphs and files open from the inline buttons alone, so selecting or expanding a
 * node never opens anything. The tree re-fetches on every expand of the root, so a plain `refresh()`
 * after `aet/storyGraphChanged` is enough to stay current.
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
            const named = element.setName !== undefined;
            const inSet = this._campaigns.filter(c => named ? c.set === element.setName : !c.set);
            return orderedDefinitions(inSet, named).map(c => this._campaignItem(c, named));
        }
        if (element.kind === 'campaign') {
            const campaign = this._campaigns.find(c => c.name === element.campaignName);
            return campaign ? orderedFactions(campaign).map(f => this._factionItem(campaign, f)) : [];
        }
        if (element.kind === 'faction') {
            const faction = this._campaigns
                .find(c => c.name === element.campaignName)?.factions
                .find(f => f.faction === element.factionName);
            if (!faction) {
                return [];
            }
            // The galactic threads first, then the battles in play order, each folding its own
            // plot files away: the galactic story does not care about a battle's inner workings,
            // and a thread listed twice would read as two files.
            const tree = factionTree(faction);
            return [
                ...tree.galacticThreads.map(t => this._threadItem(t)),
                ...tree.battles.map((branch, index) =>
                    this._battleItem(faction.faction, element.campaignName!, branch, index, tree.battles.length)),
                ...faction.luaScripts.map(s => this._luaItem(s)),
            ];
        }
        if (element.kind === 'battle') {
            const faction = this._campaigns
                .find(c => c.name === element.campaignName)?.factions
                .find(f => f.faction === element.factionName);
            const branch = faction
                ? factionTree(faction).battles.find(b => b.battle.key === element.fileName)
                : undefined;
            // The battle's plot files, then the script its manifest attaches - the mission's own
            // state machine, which the faction manifest never lists.
            return [
                ...(branch?.threads ?? []).map(t => this._threadItem(t)),
                ...(branch?.battle.luaScripts ?? []).map(s => this._luaItem(s)),
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
            if (!c.set) {
                ungrouped.push(c);
                continue;
            }
            const list = bySet.get(c.set);
            if (list) {
                list.push(c);
            } else {
                bySet.set(c.set, [c]);
            }
        }

        const items = [...bySet.keys()]
            .sort((a, b) => a.localeCompare(b))
            .map(setName => this._setItem(setName, bySet.get(setName)!));
        if (ungrouped.length) {
            items.push(this._setItem(undefined, ungrouped));
        }
        return items;
    }

    // setName undefined => the "Ungrouped" bucket.
    private _setItem(setName: string | undefined, members: StoryCampaignDto[]): StoryTreeItem {
        const label = setName ?? 'Ungrouped';
        const item = new StoryTreeItem(
            label, vscode.TreeItemCollapsibleState.Collapsed, 'set',
            undefined, undefined, undefined, setName);
        item.iconPath = new vscode.ThemeIcon(setName ? 'folder-library' : 'folder');
        item.description = setSummary(members, setName !== undefined);
        item.tooltip = setName ?? 'No Campaign_Set';
        item.contextValue = setName ? 'aetCampaignSet' : 'aetCampaignSetUngrouped';
        return item;
    }

    /** A campaign definition: inside a set, one playable perspective of the set's campaign. */
    private _campaignItem(campaign: StoryCampaignDto, inSet: boolean): StoryTreeItem {
        const {label, description} = definitionLabel(campaign, inSet);
        const item = new StoryTreeItem(
            label, vscode.TreeItemCollapsibleState.Collapsed, 'campaign', campaign.name);
        item.iconPath = new vscode.ThemeIcon('map');
        item.description = description;
        const file = campaign.definitionUri ? fileNameOf(campaign.definitionUri) : undefined;
        item.tooltip = file ? `${campaign.name} - ${file}` : campaign.name;
        item.openUri = campaign.definitionUri ?? undefined;
        item.openLine = campaign.definitionLine ?? undefined;
        item.contextValue = item.openUri ? 'aetStoryCampaign' : 'aetStoryCampaignNoFile';
        return item;
    }

    /**
     * A faction, and the level the graph opens from.
     *
     * A campaign declares a plot manifest per faction and those are separate chains, each run for
     * that faction's player - the human's for the Starting_Active_Player faction, an AI's otherwise.
     * The graph used to open on the campaign and merged them, which is why the button lives here.
     */
    private _factionItem(campaign: StoryCampaignDto, faction: StoryFactionDto): StoryTreeItem {
        const item = new StoryTreeItem(
            faction.faction, vscode.TreeItemCollapsibleState.Collapsed, 'faction',
            campaign.name, faction.faction);
        const role = factionRole(campaign, faction);
        const roleText = roleLabel(role);
        item.iconPath = new vscode.ThemeIcon(ROLE_ICONS[role.kind]);
        item.description = roleText;
        item.contextValue = 'aetStoryFaction';
        item.tooltip = (roleText ? `${faction.faction} - ${roleText}` : faction.faction)
            + `\n${faction.manifestFile}`;
        return item;
    }

    /**
     * A battle: a tactical plot manifest the galactic story links out to, and the level its own
     * graph opens from. Ordered by where the galactic story reaches it, which is what "battle 3 of
     * 9" says, since a file name says nothing about the order of play.
     */
    private _battleItem(
        factionName: string, campaignName: string, branch: BattleBranch, index: number, count: number,
    ): StoryTreeItem {
        const item = new StoryTreeItem(
            branch.battle.label, vscode.TreeItemCollapsibleState.Collapsed, 'battle',
            campaignName, factionName, branch.battle.key);
        item.iconPath = new vscode.ThemeIcon('target');
        item.contextValue = 'aetStoryBattle';
        item.description = `Battle ${index + 1} of ${count}`;
        item.tooltip = branch.battle.label
            + `\nPlot files - ${branch.threads.length}`
            + `\nScripts - ${branch.battle.luaScripts?.length ?? 0}`;
        return item;
    }

    private _threadItem(thread: StoryPlotThreadDto): StoryTreeItem {
        const item = new StoryTreeItem(
            thread.file, vscode.TreeItemCollapsibleState.None, 'thread',
            undefined, undefined, thread.file);
        item.iconPath = new vscode.ThemeIcon(thread.suspended ? 'circle-slash' : 'type-hierarchy-sub');
        item.description = thread.suspended ? 'suspended' : undefined;
        this._fileNode(item, thread.file, thread.uri, thread.suspended ? 'suspended' : undefined);
        return item;
    }

    private _luaItem(script: StoryLuaScriptDto): StoryTreeItem {
        const item = new StoryTreeItem(
            script.name, vscode.TreeItemCollapsibleState.None, 'lua',
            undefined, undefined, script.name);
        item.iconPath = new vscode.ThemeIcon('file-code');
        item.description = 'Lua';
        const file = script.name.toLowerCase().endsWith('.lua') ? script.name : `${script.name}.lua`;
        this._fileNode(item, file, script.uri, undefined);
        return item;
    }

    /**
     * A node that stands for one document: its "Open File" button, or the disabled one when the
     * story model could not read the file - a broken chain link, which the tooltip names.
     */
    private _fileNode(item: StoryTreeItem, file: string, uri: string | null | undefined, state: string | undefined): void {
        item.openUri = uri ?? undefined;
        item.contextValue = item.openUri ? 'aetStoryFile' : 'aetStoryFileMissing';
        item.tooltip = item.openUri
            ? (state ? `${file} - ${state}` : file)
            : `${file} - not found`;
    }

    private _infoItem(message: string): StoryTreeItem {
        const item = new StoryTreeItem(
            message, vscode.TreeItemCollapsibleState.None, 'info');
        item.iconPath = new vscode.ThemeIcon('info');
        return item;
    }
}

function fileNameOf(uri: string): string {
    return decodeURIComponent(uri.slice(uri.lastIndexOf('/') + 1));
}
