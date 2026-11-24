// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Everything the extension remembers between sessions, each value in its slot (#324).
//
// Built once on activation from the two mementos and handed on - a panel reaches it through its
// project - so no module keeps a memento of its own. The TIER of each value is decided here and
// nowhere else: `global` follows the person, `workspace` follows the mod. A value belongs to the
// workspace when the answer would be expected to change with the mod that is open.
//
// Free of `vscode`, so it runs under the node test harness.

import {
    mementoSlot,
    type MementoScope,
    type MementoSlot,
} from "./mementoSlot";
import {
    type EncyclopediaScreen,
    encyclopediaScreenFrom,
} from "./shared/encyclopediaScreens";
import type { StoredGeometry } from "./shared/modalGeometry";
import { type PanelLayout, panelLayoutFrom } from "./shared/panelLayout";
import {
    type ProjectSettings,
    projectSettingsFrom,
} from "./shared/projectSettings";
import { asRecord, names } from "./shared/storedValue";
import {
    type ViewerSettings,
    viewerSettingsFrom,
} from "./shared/viewerSettings";

export interface Stores {
    /**
     * Where each dialog was last put, by dialog. Per mod: a project whose credits file needs a tall
     * dialog keeps one, and opening a different mod does not inherit it.
     */
    readonly dialogGeometry: MementoSlot<Record<string, StoredGeometry>>;
    /**
     * How wide each editor's dock is and how tall its drawer, keyed `<surface>.<control>`. Per
     * person: a comfortable width is a property of the screen and the habit, not of the mod.
     */
    readonly panelLayout: MementoSlot<PanelLayout>;
    /**
     * The preview's room: the grid, the floor, the lights, the background. Per person - having to
     * set the light again in every mod is the annoyance this exists to remove.
     */
    readonly viewerSettings: MementoSlot<ViewerSettings>;
    /**
     * What the preview remembers about each project, by project key: the weapon bench, the faction
     * and its colour. Per mod and per project inside it, because every field names something out
     * of the mod's own tree - a damage type set against one mod means nothing in the next, and a
     * window holding two projects is the same fault one level down.
     *
     * There is deliberately NO migration from the earlier shapes - the global blob, then one blob
     * per window. Copying either in would carry one mod's damage types into another project.
     */
    readonly projectSettings: MementoSlot<Record<string, ProjectSettings>>;
    /** The screen the encyclopedia card is drawn for. Per person: the display the modder targets. */
    readonly encyclopediaScreen: MementoSlot<EncyclopediaScreen>;
    /** The key of the project last picked for this window, when one was. */
    readonly chosenProject: MementoSlot<string | undefined>;
    /** The folders whose project file offer was declined for good. */
    readonly projectOfferDeclined: MementoSlot<string[]>;
    /** The version whose What's New page was last shown, when one was. */
    readonly whatsNewSeenVersion: MementoSlot<string | undefined>;
}

/** @param global `context.globalState`. @param workspace `context.workspaceState`. */
export function createStores(
    global: MementoScope,
    workspace: MementoScope,
): Stores {
    return {
        dialogGeometry: mementoSlot(
            "aet-eaw-edit.dialogGeometry",
            dialogGeometryFrom,
            workspace,
        ),
        panelLayout: mementoSlot(
            "aet-eaw-edit.panelLayout",
            panelLayoutFrom,
            global,
        ),
        viewerSettings: mementoSlot(
            "aet-eaw-edit.previewViewerSettings",
            viewerSettingsFrom,
            global,
        ),
        projectSettings: mementoSlot(
            "aet-eaw-edit.previewProjectSettings.byProject",
            projectSettingsByProjectFrom,
            workspace,
        ),
        encyclopediaScreen: mementoSlot(
            "aet-eaw-edit.encyclopediaScreen",
            encyclopediaScreenFrom,
            global,
        ),
        chosenProject: mementoSlot(
            "aet-eaw-edit.chosenProject",
            optionalString,
            workspace,
        ),
        projectOfferDeclined: mementoSlot(
            "aet-eaw-edit.projectOfferDeclined",
            names,
            global,
        ),
        whatsNewSeenVersion: mementoSlot(
            "whatsNew.seenVersion",
            optionalString,
            global,
        ),
    };
}

/**
 * Records one dialog's placement. One that is not four finite numbers is dropped and the dialog
 * keeps the placement it had: the payload crosses a webview boundary, and a NaN written once would
 * come back every session and put the dialog nowhere.
 */
export function saveDialogGeometry(
    stores: Stores,
    id: string,
    geometry: unknown,
): void {
    if (isGeometry(geometry)) {
        stores.dialogGeometry.save({
            ...stores.dialogGeometry.read(),
            [id]: geometry,
        });
    }
}

/** Records one dock's or drawer's size. */
export function savePanelSize(
    stores: Stores,
    key: string,
    value: number,
): void {
    stores.panelLayout.save({ ...stores.panelLayout.read(), [key]: value });
}

/** What one project stored, defaulted field by field. */
export function readProjectSettings(
    stores: Stores,
    projectKey: string,
): ProjectSettings {
    return projectSettingsFrom(stores.projectSettings.read()[projectKey]);
}

/** Records one project's settings and leaves every other project's as they are. */
export function saveProjectSettings(
    stores: Stores,
    projectKey: string,
    settings: unknown,
): void {
    stores.projectSettings.save({
        ...stores.projectSettings.read(),
        [projectKey]: settings,
    });
}

/** The stored screen as `aet/getEncyclopediaEntry` takes it. */
export function encyclopediaScreenParams(stores: Stores): {
    screenWidth: number;
    screenHeight: number;
} {
    const screen = stores.encyclopediaScreen.read();
    return { screenWidth: screen.width, screenHeight: screen.height };
}

function dialogGeometryFrom(stored: unknown): Record<string, StoredGeometry> {
    const placed: Record<string, StoredGeometry> = {};
    for (const [id, geometry] of Object.entries(asRecord(stored))) {
        if (isGeometry(geometry)) {
            placed[id] = geometry;
        }
    }
    return placed;
}

function isGeometry(value: unknown): value is StoredGeometry {
    const candidate = asRecord(value);
    return (["xRatio", "yRatio", "width", "height"] as const).every(
        (field) =>
            typeof candidate[field] === "number" &&
            Number.isFinite(candidate[field]),
    );
}

function projectSettingsByProjectFrom(
    stored: unknown,
): Record<string, ProjectSettings> {
    const byProject: Record<string, ProjectSettings> = {};
    for (const [key, settings] of Object.entries(asRecord(stored))) {
        byProject[key] = projectSettingsFrom(settings);
    }
    return byProject;
}

function optionalString(stored: unknown): string | undefined {
    return typeof stored === "string" ? stored : undefined;
}
