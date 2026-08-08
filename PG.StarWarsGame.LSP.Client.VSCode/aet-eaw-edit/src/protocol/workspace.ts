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
