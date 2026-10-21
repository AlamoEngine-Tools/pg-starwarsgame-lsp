// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The Mod Browser's words for the keys the server answers with (#270), and the choices its search
// form offers. Pure, so it is testable without a webview.

import * as l10n from "@vscode/l10n";

import type { SymbolLayer } from "../protocol/modBrowser";

const XML_PREFIX = "xml:";

/** A list's name: the game objects' list by what it holds, every other XML type by its own name. */
export function pageLabel(page: string): string {
    switch (page) {
        case "xml:GameObjectType":
            return l10n.t("Game Objects");
        case "models":
            return l10n.t("Models");
        case "particleSystems":
            return l10n.t("Particle Systems");
        case "animations":
            return l10n.t("Animations");
        case "textures":
            return l10n.t("Textures");
        case "audio":
            return l10n.t("Audio");
        case "luaScripts":
            return l10n.t("Lua Scripts");
        case "enums":
            return l10n.t("Enums");
        default:
            return page.startsWith(XML_PREFIX)
                ? page.slice(XML_PREFIX.length)
                : page;
    }
}

/** A relation's name. */
export function relationLabel(relation: string): string {
    switch (relation) {
        case "models":
            return l10n.t("Models");
        case "particleSystems":
            return l10n.t("Particle Systems");
        case "particles":
            return l10n.t("Particles");
        case "sfxEvents":
            return l10n.t("SFX Events");
        case "speechEvents":
            return l10n.t("Speech Events");
        case "hardpoints":
            return l10n.t("Hardpoints");
        case "otherReferences":
            return l10n.t("Other references");
        case "textures":
            return l10n.t("Textures");
        case "samples":
            return l10n.t("Samples");
        case "luaScript":
            return l10n.t("Lua Script");
        case "referencedBy":
            return l10n.t("Referenced by");
        case "definitions":
            return l10n.t("Definitions");
        case "animations":
            return l10n.t("Animations");
        case "model":
            return l10n.t("Model");
        case "usedBy":
            return l10n.t("Used by");
        case "loadedBy":
            return l10n.t("Loaded by");
        case "requires":
            return l10n.t("Requires");
        default:
            return relation;
    }
}

/**
 * Where an entry comes from, as the panel names it: the root project is this mod, a dependency is
 * named, then the game. The word "layer" is the plan's, never the panel's.
 */
export function originLabel(
    origin: string,
    originName: string | null | undefined,
    layers: readonly SymbolLayer[],
): string {
    switch (origin) {
        case "game":
            return l10n.t("Game");
        case "project":
            return l10n.t("This project");
        case "missing":
            return l10n.t("Not found");
        case "schema":
            return l10n.t("Schema");
    }
    const rank = Number(origin.slice("layer:".length));
    const leaf = Math.max(-1, ...layers.map((l) => l.rank));
    if (layers.length > 1 && rank === leaf) {
        return l10n.t("This mod");
    }
    return originName ?? layers.find((l) => l.rank === rank)?.name ?? origin;
}

/** Why a relation lists nothing. */
export function reasonLabel(reason: string | null | undefined): string {
    switch (reason) {
        case "none":
            return l10n.t("None");
        case "notInModFiles":
            return l10n.t("Not in the mod's files");
        default:
            return reason ?? "";
    }
}

/** The origins the search form offers: every layer, the root project first, then the game. */
export function originChoices(layers: readonly SymbolLayer[]): string[] {
    return [
        ...[...layers]
            .sort((a, b) => b.rank - a.rank)
            .map((l) => `layer:${l.rank}`),
        "game",
    ];
}

/** The kinds the search form offers: those of the rows loaded so far, once each, by name. */
export function kindChoices(rows: readonly { kinds: string[] }[]): string[] {
    return [...new Set(rows.flatMap((r) => r.kinds))].sort((a, b) =>
        a.localeCompare(b),
    );
}
