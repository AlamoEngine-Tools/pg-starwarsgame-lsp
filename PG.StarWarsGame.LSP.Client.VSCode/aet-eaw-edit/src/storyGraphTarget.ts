// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What a story graph is OF: one campaign faction, at the galactic level or inside one of its
// battles.
//
// A campaign declares a plot manifest per faction, and those are separate chains - the playable
// faction's plots are what the player runs, and an unplayable faction's are triggered by the AI.
// The engine never runs two of them as one story. Within a faction, a tactical battle is again its
// own story: the game freezes the galaxy while it plays, and its plot files only ever run there.
// The server assembles a model per faction and serves it by scope; this is the client's half of
// that, so a panel, a title and a palette entry all speak the same unit.
//
// Pure: no `vscode` import, so it is covered by the unit harness rather than by a manual smoke run.

import {factionTree} from './storyBattles';
import type {StoryCampaignDto} from './protocol';

/**
 * One campaign faction, and optionally one of its battles - the unit a graph, a panel and a
 * simulation session are all keyed by.
 *
 * `scope` is the battle's key as the server's plots feed names it; absent means the galactic
 * level. `scopeLabel` is only for the tab title and is never part of the identity.
 */
export interface StoryGraphTarget {
    campaign: string;
    faction: string;
    scope?: string;
    scopeLabel?: string;
}

/**
 * A separator neither half can contain.
 *
 * Nothing in the XML forbids a space or a dash in a campaign name, so joining with one would let a
 * campaign called `GC - Rebel` collide with the Rebel chain of `GC`. A unit separator cannot appear
 * in either.
 */
const SEPARATOR = '';

/**
 * The registry key for one target's panel.
 *
 * Case-folded, because the campaign name is an XML attribute and the faction comes from a tag name
 * and neither is written consistently across the corpus - `Rebel_Story_Name` and a navigator entry
 * that says `rebel` have to find the same tab. The scope is folded for the same reason: it is a
 * file reference, and the corpus writes those every way.
 */
export function panelKey(target: StoryGraphTarget): string {
    const chain = `${target.campaign.toLowerCase()}${SEPARATOR}${target.faction.toLowerCase()}`;
    return target.scope ? `${chain}${SEPARATOR}${target.scope.toLowerCase()}` : chain;
}

/** The galactic target a scoped one belongs to - itself when it is already galactic. */
export function galacticOf(target: StoryGraphTarget): StoryGraphTarget {
    return {campaign: target.campaign, faction: target.faction};
}

/**
 * The tab's title. Both halves, because the faction is the half that says which chain this is; a
 * battle adds its own name after them, so two battles of one chain read apart in the tab strip.
 */
export function panelTitle(target: StoryGraphTarget): string {
    const chain = `${target.campaign} - ${target.faction}`;
    return target.scope
        ? `Battle - ${chain} - ${target.scopeLabel ?? target.scope}`
        : `Story - ${chain}`;
}

/** Every campaign faction pair the navigator feed declares, in document order. */
export function targetsOf(campaigns: readonly StoryCampaignDto[]): StoryGraphTarget[] {
    return campaigns.flatMap(campaign =>
        (campaign.factions ?? []).map(faction => ({
            campaign: campaign.name,
            faction: faction.faction,
        })));
}

/** One quick-pick entry per target, carrying the target so the caller never re-parses a label. */
export interface StoryGraphTargetItem {
    label: string;
    description: string;
    target: StoryGraphTarget;
}

/**
 * The palette fallback's list: one flat entry per faction, followed by one per battle of that
 * faction in play order, rather than a campaign prompt followed by a faction prompt.
 *
 * The tree opens a graph from its own faction or battle node, so this is the only place left that
 * has to ask at all - and asking twice for one action is what it exists to avoid. The manifest file
 * is the description because on a campaign whose factions are `Rebel` and `Empire` the manifest
 * name is what actually tells the reader which chain they are about to open; a battle's entry
 * says which battle of how many, which is what the galactic story knows about it.
 */
export function graphTargets(campaigns: readonly StoryCampaignDto[]): StoryGraphTargetItem[] {
    return campaigns.flatMap(campaign =>
        (campaign.factions ?? []).flatMap(faction => {
            const chain = `${campaign.name} - ${faction.faction}`;
            const battles = factionTree(faction).battles;
            return [
                {
                    label: chain,
                    description: faction.manifestFile,
                    target: {campaign: campaign.name, faction: faction.faction},
                },
                ...battles.map((branch, index) => ({
                    label: `${chain} - ${branch.battle.label}`,
                    description: `Battle ${index + 1} of ${battles.length}`,
                    target: {
                        campaign: campaign.name, faction: faction.faction,
                        scope: branch.battle.key, scopeLabel: branch.battle.label,
                    },
                })),
            ];
        }));
}
