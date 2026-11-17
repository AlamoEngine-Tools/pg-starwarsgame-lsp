// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The subject's state pushed into the viewport: the weapons' arcs and turrets, the reticles and
// the hardpoint health they show, the faction tint, and the playhead read back while a clip runs.

import {
    useEffect,
    type Dispatch,
    type SetStateAction,
    type RefObject,
} from "react";
import { type PreviewScene } from "../../protocol/modelPreview";
import { type PreviewViewport, type ViewportStats } from "./viewport";
import { affiliationColour, colorizationFor, parseHex } from "./colour";
import { drawsAnnotations, type PreviewMode } from "./previewMode";
import { visibleArcs, type WeaponRow } from "./weaponRows";
import { reticleMarks, reticleScreenSize, type ReticleState } from "./reticles";
import { restAim, type TurretHandle } from "./turretHandles";
import { type TurretSweep } from "./deathClone";

/** What useSubjectSync reads from the rest of the preview. */
export interface SubjectSyncInputs {
    animation: string | null;
    customColour: string | null;
    destroyed: ReadonlySet<string>;
    draggableTurrets: TurretHandle[];
    faction: string;
    fireArcs: boolean;
    hardpointHealth: Record<string, number | null>;
    hiddenWeapons: ReadonlySet<string>;
    mode: PreviewMode;
    reticleState: ReticleState;
    reticlesOn: boolean;
    scene: PreviewScene | null;
    scrubbingRef: RefObject<boolean>;
    setPlayhead: Dispatch<
        SetStateAction<{ time: number; duration: number; running: boolean }>
    >;
    stats: ViewportStats | null;
    sweepable: TurretSweep[];
    sweeping: ReadonlySet<string>;
    viewportRef: RefObject<PreviewViewport | null>;
    weapons: WeaponRow[];
}

export function useSubjectSync({
    animation,
    customColour,
    destroyed,
    draggableTurrets,
    faction,
    fireArcs,
    hardpointHealth,
    hiddenWeapons,
    mode,
    reticleState,
    reticlesOn,
    scene,
    scrubbingRef,
    setPlayhead,
    stats,
    sweepable,
    sweeping,
    viewportRef,
    weapons,
}: SubjectSyncInputs) {
    useEffect(() => {
        const viewport = viewportRef.current;
        if (viewport === null) {
            return;
        }

        // BEFORE the arcs and the handles, both of which are sized against the subject's span and
        // counter-scale against this. The subject's geometry is in MODEL units and every range
        // drawn beside it is in WORLD units; this is what reconciles them.
        viewport.setModelScale(scene?.scaleFactor ?? 1);

        // Which cones exist is decided in one place, `weaponRows`, off the same rows the dock is
        // showing - so a weapon switched off in the dock, and a hardpoint that has been shot away, are
        // the same answer in both. Weapons live on the scene rather than on the hardpoint, so a
        // unit-attached weapon draws through exactly this path too.
        //
        // ANDed with the lens: a cone is an annotation over the model rather than part of it, and
        // only Gameplay carries the pill that switches it off. Left ungated, arcs latched on in
        // Gameplay went on drawing in Model mode with nothing to press. The reader's own `fireArcs`
        // and `hiddenWeapons` are untouched, so leaving the lens and coming back finds the arcs
        // exactly as they were left.
        const annotate = drawsAnnotations(mode);

        viewport.setFireArcs(
            annotate ? visibleArcs(weapons, fireArcs, hiddenWeapons) : [],
        );

        // Still set, though the list above is already empty when the master is off: the capture
        // path hides the arcs for a screenshot and puts them back afterwards, and it restores
        // through this flag.
        viewport.setFireArcsVisible(annotate && fireArcs);

        // Turrets sit where the XML says they rest, rather than wherever the model was exported.
        // Only the turrets that declare a traverse, and only while the reader has asked. A turret
        // with no extents is left at its rest angle - inventing a swing would claim a reach the
        // unit has not got.
        // Every bone a weapon fires from, so the selected-bone triad can arrow its aim axis.
        viewport.setFireBones(
            new Set(
                weapons
                    .flatMap((row) => row.fireBones)
                    .map((bone) => bone.toLowerCase()),
            ),
        );

        viewport.setTurretSweep(
            sweepable.filter((sweep) => sweeping.has(sweep.id)),
        );

        // Gated on the lens for the same reason the arcs are: a manipulator is an annotation over
        // the model, and Gameplay is the only lens that carries the pill that switches it off.
        viewport.setTurretHandles(annotate ? draggableTurrets : []);

        // Every turret, from BOTH places one can be declared, at the rest the game steers it to -
        // a turret with nothing written rests where the model put it.
        viewport.setTurretRestAngles(
            [
                ...(scene?.hardpoints ?? []).map((hardpoint) => ({
                    partId: hardpoint.partId ?? "hull",
                    turret: hardpoint.turret,
                })),
                ...weapons.map((weapon) => ({
                    partId: weapon.partId,
                    turret: weapon.turret,
                })),
            ].flatMap(({ partId, turret }) => {
                const bone = turret?.turretBone ?? "";

                return turret === null || turret === undefined || bone === ""
                    ? []
                    : [
                          {
                              partId,
                              turretBone: bone,
                              barrelBone: turret.barrelBone ?? null,
                              rest: restAim(turret),
                          },
                      ];
            }),
        );
    }, [
        scene,
        stats,
        mode,
        fireArcs,
        hiddenWeapons,
        weapons,
        sweeping,
        sweepable,
        draggableTurrets,
    ]);

    /**
     * The targeting marks, and how big the game would draw them.
     *
     * Depends on `stats` for the same reason the arcs do: the parts a mark is centred on are not
     * there until the geometry has loaded, and a mark anchored on a part that does not exist yet is
     * silently dropped.
     */
    useEffect(() => {
        const viewport = viewportRef.current;
        if (viewport === null) {
            return;
        }

        // The hardpoint health goes in so each mark can be tinted by how worn its hardpoint is.
        //
        // ANDed with the lens, exactly as the arcs are: a targeting mark is a drawing over the
        // model saying what the game can shoot at, not a part of the asset, and only Gameplay
        // carries the pill that puts it away.
        const marks =
            reticlesOn && drawsAnnotations(mode)
                ? reticleMarks(
                      scene?.hardpoints ?? [],
                      scene?.reticles,
                      reticleState,
                      destroyed,
                      hardpointHealth,
                  )
                : [];

        viewport.setReticles(
            marks,
            reticleScreenSize(scene?.reticles, reticleState),
        );
    }, [
        scene,
        stats,
        mode,
        reticlesOn,
        reticleState,
        destroyed,
        hardpointHealth,
    ]);

    useEffect(() => {
        const chosen =
            customColour !== null
                ? parseHex(customColour)
                : (scene?.factions.find((f) => f.name === faction)?.color ??
                  null);

        // With no faction picked the subject wears its OWN uncoloured colour, and failing that its
        // faction's: team colour is a skirmish thing, so this is what the unit looks like most of
        // the time. Only 24 of the 772 objects that name an affiliation carry a colour of their
        // own, so the fallback is what actually reaches most of them.
        viewportRef.current?.setColorization(
            colorizationFor(
                chosen,
                scene?.noColorizationColor,
                affiliationColour(scene?.factions ?? [], scene?.affiliation),
            ),
        );
    }, [faction, customColour, scene, stats]);

    /**
     * Follows the playhead while a clip is loaded.
     *
     * A timer rather than a hook into the render loop: the scrubber only has to look continuous to
     * a person, and 20 a second does that for a tenth of the re-renders 60 would cost. It runs only
     * in Animation mode, so the other lenses pay nothing for a control they do not show.
     */
    useEffect(() => {
        if (mode !== "animation" || animation === null) {
            return;
        }

        const tick = setInterval(() => {
            if (!scrubbingRef.current) {
                setPlayhead(
                    viewportRef.current?.animationProgress() ?? {
                        time: 0,
                        duration: 0,
                        running: false,
                    },
                );
            }
        }, 50);

        return () => clearInterval(tick);
    }, [mode, animation]);

    return {};
}
