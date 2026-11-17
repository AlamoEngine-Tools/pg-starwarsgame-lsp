// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The overlay pills on the stage top edge: grid, floor and wireframe always, Effects disabled
// rather than removed, and Arcs and Reticles last, in the Gameplay lens only.

import * as l10n from "@vscode/l10n";
import { type PreviewScene } from "../../protocol/modelPreview";
import { type IconName } from "../shared/Icon";
import { type ParticleGroup } from "./particleScene";
import { type PreviewMode } from "./previewMode";
import { allWeaponIds, type WeaponRow } from "./weaponRows";
import { reticleMarks, type ReticleState } from "./reticles";
import { type Dispatch, type SetStateAction } from "react";

/** What useOverlayPills reads from the rest of the preview. */
export interface OverlayPillsInputs {
    emitters: string[];
    fireArcs: boolean;
    floor: boolean;
    grid: boolean;
    groups: ParticleGroup[];
    hiddenWeapons: ReadonlySet<string>;
    mode: PreviewMode;
    particlesOn: boolean;
    reticleState: ReticleState;
    reticlesOn: boolean;
    scene: PreviewScene | null;
    setFireArcs: Dispatch<SetStateAction<boolean>>;
    setFloor: Dispatch<SetStateAction<boolean>>;
    setGrid: Dispatch<SetStateAction<boolean>>;
    setHiddenWeapons: Dispatch<SetStateAction<ReadonlySet<string>>>;
    setParticlesOn: Dispatch<SetStateAction<boolean>>;
    setReticlesOn: Dispatch<SetStateAction<boolean>>;
    setWireframe: Dispatch<SetStateAction<boolean>>;
    weapons: WeaponRow[];
    wireframe: boolean;
}

export type OverlayPills = ReturnType<typeof useOverlayPills>;

export function useOverlayPills({
    emitters,
    fireArcs,
    floor,
    grid,
    groups,
    hiddenWeapons,
    mode,
    particlesOn,
    reticleState,
    reticlesOn,
    scene,
    setFireArcs,
    setFloor,
    setGrid,
    setHiddenWeapons,
    setParticlesOn,
    setReticlesOn,
    setWireframe,
    weapons,
    wireframe,
}: OverlayPillsInputs) {
    /**
     * Everything the viewport can be asked to draw or not draw, as one strip of pills.
     *
     * They are gathered here rather than written out in the markup because which ones apply depends
     * on the lens and on the subject - the skeleton is Model's business, firing arcs are Gameplay's,
     * and particles only exist if the model has emitters. Assembling the list keeps that reasoning
     * in one place instead of scattering five conditions through the panel.
     */
    const overlays: {
        id: string;
        label: string;
        icon: IconName;
        title: string;
        on: boolean;
        set: (on: boolean) => void;
        disabled?: boolean;
    }[] = [
        {
            id: "grid",
            label: l10n.t("Grid"),
            icon: "grid",
            on: grid,
            set: setGrid,
            title: l10n.t("Show ground grid and world axes"),
        },
        {
            id: "floor",
            label: l10n.t("Ground"),
            icon: "ground",
            on: floor,
            set: setFloor,
            title: l10n.t("Show lit floor"),
        },
        {
            // With the view toggles rather than in the scene panel: this is flipped constantly
            // while reading geometry, the same way the grid is, and it describes how the model is
            // DRAWN rather than what the room is like.
            id: "wireframe",
            label: l10n.t("Wireframe"),
            icon: "geometry",
            on: wireframe,
            set: setWireframe,
            title: l10n.t("Draw meshes as edges"),
        },
    ];

    // Never removed. A control that is absent on one model and present on the next reads as a
    // feature that comes and goes; disabled says "this exists, and here is why it is not available".
    const anyEffect = emitters.length > 0 || groups.length > 0;

    overlays.push({
        id: "particles",
        label: l10n.t("Effects"),
        icon: "effects",
        on: particlesOn && anyEffect,
        set: setParticlesOn,
        disabled: !anyEffect,
        title: !anyEffect
            ? l10n.t("No particle systems on this model")
            : l10n.t("Show every particle system"),
    });
    // Arcs LAST, and deliberately so. It is the one pill here that still comes and goes - it
    // belongs to the Gameplay lens, which is a statement about which lens you are in rather than
    // about this model - so it sits at the end of the strip where arriving and leaving cannot move
    // the three permanent pills beside it.
    if (mode === "gameplay") {
        // Whether anything can be DRAWN, not whether the subject is armed. A weapon that declares
        // no reach has no cone, so a pill that lit up for it would switch on nothing at all.
        const drawable = weapons.filter((row) => row.arcs.length > 0);
        const anyArc = drawable.length > 0;
        const shown = drawable.filter(
            (row) => !row.destroyed && !hiddenWeapons.has(row.id),
        ).length;

        overlays.push({
            // All or nothing: the pill IS the weapons, so pressing it writes every one of them and
            // the list below is for refining afterwards. It does not preserve a previous selection
            // across a press - that is the point of an all-or-nothing master.
            id: "arcs",
            label: l10n.t("Arcs"),
            icon: "arcs",
            on: fireArcs && anyArc,
            set: (on: boolean) => {
                setFireArcs(on);
                setHiddenWeapons(on ? new Set() : allWeaponIds(weapons));
            },
            disabled: !anyArc,
            title: !anyArc
                ? l10n.t("No firing arcs on this model")
                : shown === drawable.length
                  ? l10n.t("Show every firing arc")
                  : l10n.t(
                        "Show firing arcs - {0} of {1} weapons on",
                        shown,
                        drawable.length,
                    ),
        });

        // Whether a mark can be DRAWN, which is a different question from whether the subject has
        // targetable hardpoints: the art resolves out of the game's texture directory, so a workspace
        // with no game directory configured gets the map and no images. Saying so on the pill beats
        // a switch that lights up and changes nothing.
        const targetable = (scene?.hardpoints ?? []).filter(
            (h) => h.isTargetable,
        ).length;
        const anyReticle =
            reticleMarks(
                scene?.hardpoints ?? [],
                scene?.reticles,
                reticleState,
                new Set(),
            ).length > 0;

        overlays.push({
            id: "reticles",
            label: l10n.t("Reticles"),
            icon: "target",
            on: reticlesOn && anyReticle,
            set: setReticlesOn,
            disabled: !anyReticle,
            title: anyReticle
                ? l10n.t("Show targeting marks")
                : targetable === 0
                  ? l10n.t("Nothing here can be targeted")
                  : l10n.t("Reticle art unreadable - game directory not set"),
        });
    }

    return {
        overlays,
    };
}
