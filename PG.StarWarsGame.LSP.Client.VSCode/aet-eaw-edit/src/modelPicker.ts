// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as l10n from "@vscode/l10n";
// The "Preview Model" picker's items and its choice. Kept free of `vscode` so the unit harness can
// run it; extension.ts owns the QuickPick itself.

import type { PreviewSubject } from "./modelPreviewPanel";
import { type PreviewModelEntry } from "./protocol/modelPreview";

/** One row of the picker. Structurally a `vscode.QuickPickItem`. */
export interface ModelPickItem {
    label: string;
    description: string;
}

/** The picker's rows, in the server's order. */
export function modelPickItems(
    models: readonly PreviewModelEntry[],
): ModelPickItem[] {
    return models.map((m) => ({
        label: m.name,
        description: m.baseGame ? l10n.t("Base game") : l10n.t("Project"),
    }));
}

/**
 * The model to open: the highlighted row, else what was typed, else nothing.
 *
 * Typed text is a fallback rather than an error because the list is the asset catalog, and the
 * catalog can lag the disk - a model saved a moment ago must still open by name, as it did before
 * the list existed.
 */
export function pickedModelName(
    active: { label: string } | undefined,
    typed: string,
): string | null {
    if (active !== undefined) {
        return active.label;
    }

    const name = typed.trim();
    return name === "" ? null : name;
}

/**
 * What a named model or animation previews as. An animation names no model, so it goes to the
 * server as one: it pairs the clip with its model and opens the model with the clip selected.
 */
export function previewSubjectFor(reference: string): PreviewSubject {
    return reference.toLowerCase().endsWith(".ala")
        ? { kind: "animation", animationReference: reference }
        : { kind: "model", modelReference: reference };
}
