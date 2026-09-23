// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The campaign navigator's reading of the plots feed: who plays which faction, and how a
// Campaign_Set is shown. Pure: no `vscode` import, so the unit harness covers it.
//
// Measured against the engine: Starting_Active_Player names the human's faction, and a Campaign_Set
// picks its member by it - a set is ONE campaign with a definition per playable faction.
// AI_Player_Control's type "Human" (any casing) removes the AI; every other type creates one, and
// "None" is a real AI player type (Ai/Players/Player_none.xml), not the absence of one.

import type {StoryCampaignDto, StoryFactionDto} from './protocol';

export type FactionRole =
    | { kind: 'player'; aiType?: string }
    | { kind: 'human' }
    | { kind: 'ai'; type: string }
    | { kind: 'unset' };

function sameFaction(a: string, b: string): boolean {
    return a.localeCompare(b, undefined, {sensitivity: 'accent'}) === 0;
}

/**
 * The faction's role in the campaign: the player first, then what its control pair says. The
 * player keeps an AI type when it is given one - Assign_AI_Control runs for every active player, so
 * the human's faction can carry e.g. SandboxHuman for perception and the rest of the AI setup.
 */
export function factionRole(campaign: StoryCampaignDto, faction: StoryFactionDto): FactionRole {
    const control = faction.control?.trim();
    const isAi = !!control && !sameFaction(control, 'Human');
    if (campaign.playerFaction && sameFaction(campaign.playerFaction, faction.faction)) {
        return isAi ? {kind: 'player', aiType: control} : {kind: 'player'};
    }
    if (!control) {
        return {kind: 'unset'};
    }
    return isAi ? {kind: 'ai', type: control} : {kind: 'human'};
}

/** The role as a node description; nothing when the campaign says nothing about the faction. */
export function roleLabel(role: FactionRole): string | undefined {
    switch (role.kind) {
        case 'player':
            return role.aiType ? `Player, AI - ${role.aiType}` : 'Player';
        case 'human':
            return 'Human';
        case 'ai':
            return `AI - ${role.type}`;
        default:
            return undefined;
    }
}

/** The campaign's plot factions, the player's first and the rest in the order they are declared. */
export function orderedFactions(campaign: StoryCampaignDto): StoryFactionDto[] {
    const isPlayer = (f: StoryFactionDto) => factionRole(campaign, f).kind === 'player';
    return [...campaign.factions.filter(isPlayer), ...campaign.factions.filter(f => !isPlayer(f))];
}

/**
 * Whether a set's members are perspectives of one campaign: every one names the faction it is
 * played as. A multiplayer set groups maps instead, none of which names a player.
 */
function isPerspectiveSet(members: StoryCampaignDto[], named: boolean): boolean {
    return named && members.length > 0 && members.every(c => !!c.playerFaction);
}

/**
 * A set node's description. `named` is false for the bucket of campaigns declaring no set, which
 * is a list of unrelated campaigns rather than one.
 */
export function setSummary(members: StoryCampaignDto[], named: boolean): string {
    if (isPerspectiveSet(members, named)) {
        const count = new Set(members.map(c => c.playerFaction!.toLowerCase())).size;
        return count === 1 ? '1 faction' : `${count} factions`;
    }
    return members.length === 1 ? '1 campaign' : `${members.length} campaigns`;
}

/** A definition node's label: the faction it is played as inside a set, else its own name. */
export function definitionLabel(campaign: StoryCampaignDto, inSet: boolean): { label: string; description?: string } {
    return inSet && campaign.playerFaction
        ? {label: campaign.playerFaction, description: campaign.name}
        : {label: campaign.name, description: undefined};
}

/** A set's members sorted by the label each is shown under. */
export function orderedDefinitions(members: StoryCampaignDto[], inSet: boolean): StoryCampaignDto[] {
    return [...members].sort((a, b) =>
        definitionLabel(a, inSet).label.localeCompare(definitionLabel(b, inSet).label));
}
