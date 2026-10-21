// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The Mod Browser's words for the keys the server answers with (#270), and the choices its search
// form offers. Pure, so it is testable without a webview.

import * as l10n from "@vscode/l10n";

import type {
    SymbolAction,
    SymbolKindCount,
    SymbolLayer,
    SymbolLocation,
} from "../protocol/modBrowser";

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

/** Where a definition is, as the header names it: the file and the one-based line. */
export function locationLabel(location: SymbolLocation): string {
    const name = location.uri.slice(location.uri.lastIndexOf("/") + 1);
    return `${decodeURIComponent(name)}:${location.line + 1}`;
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

/** One kind the search form offers. */
export interface KindChoice {
    kind: string;
    label: string;
    /** The list holds none of it; a picked kind stays enabled, so it can be unpicked. */
    disabled: boolean;
}

/** The kinds the search form offers: every kind the server counted, in its order. */
export function kindChoices(
    kinds: readonly SymbolKindCount[],
    picked: readonly string[],
): KindChoice[] {
    return kinds.map((k) => ({
        kind: k.kind,
        label: `${k.kind} (${k.count.toLocaleString()})`,
        disabled: k.count === 0 && !picked.includes(k.kind),
    }));
}

/** One item of an entry's menu. */
export interface ActionChoice {
    action: string;
    label: string;
    disabled: boolean;
    /** Why it is disabled. */
    reason?: string;
    /** The key that does the same. */
    keys?: string;
}

/**
 * An entry's menu, in groups: open and go to definition, then what the server says the entry offers,
 * then copying its name. Before the server has answered, go to definition waits for it; a row that
 * is no entry of its own - a missing name, a definition place - offers only its name.
 */
export function actionChoices(
    canOpen: boolean,
    actions: readonly SymbolAction[] | undefined,
): ActionChoice[][] {
    const choice = (
        action: string,
        reason: string | null | undefined,
        keys?: string,
    ): ActionChoice => ({
        action,
        label: actionLabel(action),
        disabled: !!reason,
        reason: reason ? actionReasonLabel(reason) : undefined,
        keys,
    });
    const disabled = (item: ActionChoice, reason: string): ActionChoice => ({
        ...item,
        disabled: true,
        reason,
    });
    if (!canOpen) {
        const none = l10n.t("Not an entry of its own");
        return [
            [
                disabled(choice("open", null, "Enter"), none),
                disabled(choice("definition", null, "F12"), none),
            ],
            [choice("copyName", null)],
        ];
    }
    const definition = actions?.find((a) => a.action === "definition");
    const offered = (actions ?? []).filter((a) => a.action !== "definition");
    return [
        [
            choice("open", null, "Enter"),
            definition
                ? choice("definition", definition.reason, "F12")
                : disabled(
                      choice("definition", null, "F12"),
                      l10n.t("Loading..."),
                  ),
        ],
        ...(offered.length
            ? [offered.map((a) => choice(a.action, a.reason))]
            : []),
        [choice("copyName", null)],
    ];
}

function actionLabel(action: string): string {
    switch (action) {
        case "open":
            return l10n.t("Open");
        case "definition":
            return l10n.t("Go to definition");
        case "previewUnit":
            return l10n.t("Preview unit");
        case "encyclopedia":
            return l10n.t("Show encyclopedia card");
        case "effectiveObject":
            return l10n.t("Show effective object");
        case "previewModel":
            return l10n.t("Preview model");
        case "copyName":
            return l10n.t("Copy name");
        default:
            return action;
    }
}

/** Why an action is unavailable. */
function actionReasonLabel(reason: string): string {
    switch (reason) {
        case "shipped":
            return l10n.t("Only the game defines it");
        case "binary":
            return l10n.t("Not a text file");
        case "schema":
            return l10n.t("Defined by the schema");
        case "noModel":
            return l10n.t("Names no model");
        case "noEncyclopedia":
            return l10n.t("No encyclopedia text");
        case "notAVariant":
            return l10n.t("Not a variant");
        case "off":
            return l10n.t("Turned off in the settings");
        default:
            return reason;
    }
}
