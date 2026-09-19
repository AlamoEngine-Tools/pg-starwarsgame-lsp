// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

/**
 * The launch-layers answer from the server and the two things a debug configuration is built
 * from it: the script roots the adapter maps game paths with, and the mod chain the game is
 * started with. Pure: no VS Code, no file system, so it runs under the node test harness.
 */

/** One project layer as `aet/getLaunchLayers` reports it, highest precedence first. */
export interface LaunchLayer {
    readonly name: string;
    readonly rank: number;
    readonly projectPath: string | null;
    readonly projectDirectory: string | null;
    readonly scriptRoots: readonly string[];
    /** The directory the game can take as this layer's mod, or null with a reason. */
    readonly modPath: string | null;
    readonly notRunnableReason: string | null;
}

export interface LaunchLayersResult {
    readonly enabled: boolean;
    readonly layers: readonly LaunchLayer[];
}

export type ModChain =
    | { readonly ok: true; readonly args: readonly string[] }
    | { readonly ok: false; readonly message: string };

/** Every layer's script roots in precedence order: the mod's first, its dependencies after. */
export function collectSourceRoots(layers: readonly LaunchLayer[]): string[] {
    const seen = new Set<string>();
    const roots: string[] = [];
    for (const layer of byPrecedence(layers)) {
        for (const root of layer.scriptRoots) {
            const key = root.replace(/\\/g, '/').toLowerCase();
            if (!seen.has(key)) {
                seen.add(key);
                roots.push(root);
            }
        }
    }
    return roots;
}

/**
 * The `MODPATH=` arguments for a launch. The game consults mod paths in command-line order and
 * takes the first match, so the chain runs leaf first: the mod, then each dependency in
 * precedence order. Every layer has to be runnable as it stands; the first one that is not
 * refuses the whole launch with its reason, because a chain with a hole silently plays the wrong
 * files. An explicit override replaces the chain as given, for mod folders maintained by hand.
 */
export function buildModChain(layers: readonly LaunchLayer[], override?: readonly string[]): ModChain {
    if (override !== undefined) {
        const withSpace = override.find(p => p.includes(' '));
        if (withSpace !== undefined) {
            return {
                ok: false,
                message: `The mod path '${withSpace}' contains a space; the game's command line cannot carry a path with one`
            };
        }
        return {ok: true, args: override.map(p => `MODPATH=${p}`)};
    }

    const ordered = byPrecedence(layers);
    if (ordered.length === 0) {
        return {
            ok: false,
            message: 'No mod project is loaded, so there is nothing to launch the game with. Open a workspace with a .pgproj, or set "modPaths" in the launch configuration'
        };
    }

    const args: string[] = [];
    for (const layer of ordered) {
        if (layer.modPath === null) {
            return {
                ok: false,
                message: `Project layer '${layer.name}' cannot be launched as a mod: ${layer.notRunnableReason ?? 'no reason given'}. ` +
                    'Attach to a running game instead, or set "modPaths" in the launch configuration to a mod folder you maintain yourself',
            };
        }
        args.push(`MODPATH=${layer.modPath}`);
    }
    return {ok: true, args};
}

function byPrecedence(layers: readonly LaunchLayer[]): LaunchLayer[] {
    return [...layers].sort((a, b) => b.rank - a.rank);
}
