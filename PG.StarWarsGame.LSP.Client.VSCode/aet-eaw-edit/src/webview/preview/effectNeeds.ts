// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What has to be in hand before a unit is shown, read off the data rather than off the scene graph.
//
// An effect started by name - a hardpoint's death explosion, the hull's damage smoke, the unit's own
// death blast, the fire a wreck trails - was asked for at the moment it was started, and its
// textures and effects only once an emitter of it was in the scene to name them. MEASURED on a
// mod's capital ship with every hardpoint destroyed at once: 145 requests for the one explosion,
// answered one by one over ten seconds, and its five textures asked for after the last of them. So
// the names are read here from the scene the server sent, and a system's textures and effects from
// the system itself, with no emitter built: everything is asked for while the cover is still down,
// and nothing is started until the reader starts it. The same for the wreck a hardpoint sheds: its
// model is asked for with the scene, and what the model is drawn with is read off its node tree.

import * as THREE from "three";

import type { AlamoParticleContent } from "../../protocol/modelPreview";
import { PARTICLE_EFFECT_BY_MODE } from "../../shared/baseShaders";
import { HEAT_COMPOSITE_EFFECT } from "./heatPass";
import { collectTextureNames, type MaterialExtras } from "./materials";
import { emitterEffectName, emitterTextureNames } from "./particleEffects";

/** The part of a scene that names effects something starts later. Structural: a scene is one. */
export interface SceneEffects {
    hardpoints?: readonly { deathExplosionParticles?: string | null }[];
    damagedSmokeAsset?: string | null;
    deathExplosions?: string | null;
    spinAway?: { explosion?: string | null } | null;
    deathClones?: readonly {
        modelFile?: string | null;
        endExplosions?: readonly string[];
        particles?: readonly { systemRef: string }[];
    }[];
    breakoffProps?: readonly {
        resolved: boolean;
        modelRef?: string | null;
        attachedParticle?: string | null;
        deathExplosions?: string | null;
        particles?: readonly { systemRef: string }[];
    }[];
}

/**
 * Every effect the scene can start by name, each once, in the order the scene names them.
 *
 * A name is one effect however it is cased. What a wreck or a death clone carries counts only
 * where there is a model to drop: a prop that is named but not defined is never dropped.
 */
export function sceneEffectNames(scene: SceneEffects): string[] {
    const names = new Map<string, string>();
    const add = (name: string | null | undefined): void => {
        const trimmed = (name ?? "").trim();
        if (trimmed !== "" && !names.has(trimmed.toLowerCase())) {
            names.set(trimmed.toLowerCase(), trimmed);
        }
    };

    for (const hardpoint of scene.hardpoints ?? []) {
        add(hardpoint.deathExplosionParticles);
    }

    add(scene.damagedSmokeAsset);
    add(scene.deathExplosions);
    add(scene.spinAway?.explosion);

    for (const clone of scene.deathClones ?? []) {
        // How the clone ends is its type's own, played whether or not a wreck was drawn.
        for (const name of clone.endExplosions ?? []) {
            add(name);
        }

        if ((clone.modelFile ?? "") !== "") {
            for (const particle of clone.particles ?? []) {
                add(particle.systemRef);
            }
        }
    }

    for (const prop of scene.breakoffProps ?? []) {
        if (!prop.resolved || (prop.modelRef ?? "") === "") {
            continue;
        }

        add(prop.attachedParticle);
        add(prop.deathExplosions);
        for (const particle of prop.particles ?? []) {
            add(particle.systemRef);
        }
    }

    return [...names.values()];
}

/** Every texture a system's emitters sample, lower-cased and each once - normal maps too. */
export function systemTextureNames(system: AlamoParticleContent): string[] {
    return [
        ...new Set(
            system.emitters
                .flatMap(emitterTextureNames)
                .map((name) => name.toLowerCase())
                .filter((name) => name !== ""),
        ),
    ];
}

/**
 * Every engine effect a system is drawn with, each once and sorted.
 *
 * A heat sprite names none of its own - the distortion pass draws it - but it bends the frame by
 * the strength the heat composite declares, so a system with heat in it, by the flag or by the
 * Heat render mode, needs that effect too.
 */
export function systemEffectNames(system: AlamoParticleContent): string[] {
    const names = new Set<string>();

    for (const emitter of system.emitters) {
        const name = emitterEffectName(emitter);
        if (name !== null) {
            names.add(name);
        }
    }

    const heatMode = PARTICLE_EFFECT_BY_MODE.get("Heat");
    if (
        system.emitters.some((emitter) => emitter.properties.isHeatParticle) ||
        (heatMode !== undefined && names.has(heatMode))
    ) {
        names.add(HEAT_COMPOSITE_EFFECT);
    }

    return [...names].sort();
}

/** The part of a scene that names the wrecks its hardpoints shed. Structural: a scene is one. */
export interface SceneWrecks {
    breakoffProps?: readonly {
        id: string;
        resolved: boolean;
        modelRef?: string | null;
        animations?: readonly string[];
    }[];
}

/** A wreck's model, as it is asked for: by the prop that names it, with that model's own clips. */
export interface WreckModel {
    id: string;
    modelReference: string;
    animations: string[];
}

/**
 * The model of every wreck the scene can drop.
 *
 * One per prop. Two props of one model are one request all the same: the model store sends the
 * first and answers the second with its reply.
 */
export function sceneWreckModels(scene: SceneWrecks): WreckModel[] {
    return (scene.breakoffProps ?? [])
        .filter((prop) => prop.resolved && (prop.modelRef ?? "") !== "")
        .map((prop) => ({
            id: prop.id,
            modelReference: prop.modelRef ?? "",
            animations: [...(prop.animations ?? [])],
        }));
}

/**
 * The textures and the effects a model's meshes are drawn with, read off its node tree.
 *
 * For a model that is held and not yet in the scene, where the scene graph cannot be asked. The
 * same two readings the scene graph gives for a part that is.
 */
export function modelNeeds(root: THREE.Object3D): {
    textures: string[];
    shaders: string[];
} {
    const extras: MaterialExtras[] = [];

    root.traverse((node) => {
        if (node instanceof THREE.Mesh && node.userData.alamo !== undefined) {
            extras.push(node.userData.alamo as MaterialExtras);
        }
    });

    return {
        textures: collectTextureNames(extras),
        shaders: [
            ...new Set(
                extras
                    .map((entry) => entry.alamoShader ?? "")
                    .filter((name) => name !== ""),
            ),
        ].sort(),
    };
}
