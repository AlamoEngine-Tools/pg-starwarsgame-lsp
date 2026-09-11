// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What a story graph is OF: one campaign faction.
//
// A campaign declares a plot manifest per faction, and those are separate chains - the playable
// faction's plots are what the player runs, and an unplayable faction's are triggered by the AI.
// The engine never runs two of them as one story. The server assembles a model per pair; this is
// the client's half of that, so a panel, a title and a palette entry all speak the same unit.
//
// Pure: no `vscode` import, so it is covered by the unit harness rather than by a manual smoke run.

import type { StoryCampaignDto } from './protocol';

/** One campaign faction - the unit a graph, a panel and a simulation session are all keyed by. */
export interface StoryGraphTarget {
    campaign: string;
    faction: string;
}

/**
 * A separator neither half can contain.
 *
 * Nothing in the XML forbids a space or a dash in a campaign name, so joining with one would let a
 * campaign called `GC - Rebel` collide with the Rebel chain of `GC`. A unit separator cannot appear
 * in either.
 */
const SEPARATOR = '';

/**
 * The registry key for one target's panel.
 *
 * Case-folded, because the campaign name is an XML attribute and the faction comes from a tag name
 * and neither is written consistently across the corpus - `Rebel_Story_Name` and a navigator entry
 * that says `rebel` have to find the same tab.
 */
export function panelKey(target: StoryGraphTarget): string {
    return `${target.campaign.toLowerCase()}${SEPARATOR}${target.faction.toLowerCase()}`;
}

/** The tab's title. Both halves, because the faction is the half that says which chain this is. */
export function panelTitle(target: StoryGraphTarget): string {
    return `Story: ${target.campaign} - ${target.faction}`;
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
 * The palette fallback's list: one flat entry per faction rather than a campaign prompt followed
 * by a faction prompt.
 *
 * The tree opens a graph from its own faction node, so this is the only place left that has to ask
 * at all - and asking twice for one action is what it exists to avoid. The manifest file is the
 * description because on a campaign whose factions are `Rebel` and `Empire` the manifest name is
 * what actually tells the reader which chain they are about to open.
 */
export function graphTargets(campaigns: readonly StoryCampaignDto[]): StoryGraphTargetItem[] {
    return campaigns.flatMap(campaign =>
        (campaign.factions ?? []).map(faction => ({
            label: `${campaign.name} - ${faction.faction}`,
            description: faction.manifestFile,
            target: { campaign: campaign.name, faction: faction.faction },
        })));
}
