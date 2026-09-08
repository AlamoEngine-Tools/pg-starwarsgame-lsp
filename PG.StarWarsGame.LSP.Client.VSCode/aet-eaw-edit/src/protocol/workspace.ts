// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Workspace-wide requests that belong to no single editor. Mirrors
// PG.StarWarsGame.LSP.Server/WorkspaceSettingsHandlers.cs and Variants/GetEffectiveObjectParams.cs.

// ── aet/getWorkspaceSettings / aet/setWorkspaceSettings ──────────────────────

/**
 * Preferences that live with the workspace (.aetswg) rather than with VS Code.
 *
 * They are about the mod being edited, not about the machine editing it - which lane layout suits
 * a campaign travels with the campaign.
 */
export interface WorkspaceSettingsDto {
    skipStoryDeleteConfirmation: boolean;
    showThreadLanes: boolean;
    showChapterLanes: boolean;
}

/** A partial update - only the fields present are applied, the rest keep their value. */
export interface SetWorkspaceSettingsParams {
    skipStoryDeleteConfirmation?: boolean;
    showThreadLanes?: boolean;
    showChapterLanes?: boolean;
}

// ── aet/getEffectiveObject - variant inheritance preview ─────────────────────

/**
 * @param cyclic Whether the variant chain loops. `cycleObjectId` names where.
 * @param chain The objects merged to produce `xml`, base first.
 */
export interface GetEffectiveObjectResult {
    found: boolean;
    cyclic: boolean;
    cycleObjectId?: string;
    chain: string[];
    xml: string;
    typeName?: string;
}

// ── aet/resolveReference - go-to for a value a panel holds by name ───────────

/**
 * Where a reference value is defined.
 *
 * `textDocument/definition` is still the right request wherever a position exists. This one is for
 * the panels: a webview row holds a NAME and nothing else - no document, no offset - because the
 * server assembled that row out of several files.
 *
 * `error` is not decoration. "defined in the base game" and "does not resolve to any known
 * definition" mean very different things to someone editing a mod, and a panel that shows neither
 * leaves the reader hunting for a file that was never theirs.
 */
export interface ResolveReferenceParams {
    value: string;
    /** The schema's `referenceType`, e.g. `SpecialAbility`. A hint; unknown names resolve untyped. */
    referenceType?: string;
}

export interface ResolveReferenceResult {
    uri?: string | null;
    line: number;
    column: number;
    error?: string | null;
}
