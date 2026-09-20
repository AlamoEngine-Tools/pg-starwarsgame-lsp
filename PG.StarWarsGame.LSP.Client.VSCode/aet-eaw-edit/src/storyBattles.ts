// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// A faction's story split the way the game plays it: the galactic level, and the tactical battles
// it links out to, each a sub-graph of its own behind a portal.
//
// The server decides the split (which plot files a battle's manifest claims, and the order the
// galactic story reaches the battles in); this is the client's reading of that answer for the
// navigator and for the portals. Pure: no `vscode` import, so the unit harness covers it.

import type {StoryBattleDto, StoryFactionDto, StoryPlotThreadDto} from './protocol';

/** One battle with the plot threads its manifest claims, as the navigator lists them. */
export interface BattleBranch {
    battle: StoryBattleDto;
    threads: StoryPlotThreadDto[];
}

/** A faction's threads sorted into the galactic level and its battles, in play order. */
export interface FactionTree {
    galacticThreads: StoryPlotThreadDto[];
    battles: BattleBranch[];
}

/**
 * The id prefix of a battle's stand-in on the galactic graph. The server names the node
 * `tactical#<battle key>`, so the key that opens the battle's own panel is in the id itself.
 */
const TACTICAL_STUB_PREFIX = 'tactical#';

/**
 * A faction's threads at the galactic level and its battles with theirs.
 *
 * The server hands each battle its own plot files, resolved the way the model resolved them, since
 * the faction manifest never lists a tactical manifest's threads. The faction's list is the
 * galactic level as the feed gave it; a thread that also appears under a battle is left to the
 * battle, compared by document, so a file is never listed twice. A thread the server could not
 * resolve stays where the feed put it: dropping it would hide a broken chain link. Battles come back
 * in play order (the server's rank), not in the order the feed happened to list them.
 */
export function factionTree(faction: StoryFactionDto): FactionTree {
    const battles = [...(faction.battles ?? [])].sort((a, b) => a.rank - b.rank);
    const branches: BattleBranch[] = battles.map(battle => ({battle, threads: [...battle.threads]}));
    const claimed = new Set<string>();
    for (const branch of branches) {
        for (const thread of branch.threads) {
            if (thread.uri) {
                claimed.add(thread.uri.toLowerCase());
            }
        }
    }
    const galacticThreads = faction.threads.filter(t => !(t.uri && claimed.has(t.uri.toLowerCase())));
    return {galacticThreads, battles: branches};
}

/**
 * A battle's display name from any way its manifest is referred to: the tactical stub's label
 * (the reference as the XML wrote it, path and extension included), the plots feed's key, or the
 * feed's own label. The server names a battle by its manifest's file name without the extension,
 * and a panel opened from a portal must carry the same title as one opened from the navigator.
 */
export function battleLabelOf(reference: string): string {
    const file = reference.split(/[\\/]/).pop() ?? reference;
    return file.replace(/\.[^.]+$/, '');
}

/** The battle key a tactical stub node stands for, or undefined for any other node. */
export function battleKeyOfNode(nodeId: string): string | undefined {
    return nodeId.startsWith(TACTICAL_STUB_PREFIX)
        ? nodeId.slice(TACTICAL_STUB_PREFIX.length)
        : undefined;
}
