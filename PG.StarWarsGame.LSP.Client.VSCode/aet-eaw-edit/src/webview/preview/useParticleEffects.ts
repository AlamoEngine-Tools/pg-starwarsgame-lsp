// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The particle systems: which have arrived, which attachments wait on them, and the callbacks that
// decide whether an effect draws now and put it on the model. One hook so the message handler, the
// combat lens and the emitter rows reach one set of maps.

import {
    useCallback,
    useRef,
    type Dispatch,
    type SetStateAction,
    type RefObject,
} from "react";
import {
    type AlamoParticleContent,
    type PreviewParticle,
} from "../../protocol/modelPreview";
import { type PreviewViewport } from "./viewport";
import { type PreviewAssetLoader } from "./previewAssetLoader";
import { effectPlaysNow } from "./damage";
import { type SceneEffects, sceneEffectNames } from "./effectNeeds";
import { passiveEffects } from "./passiveSubject";
import { effectState } from "./visibility";

/** An effect asked for before its system arrived, and where it goes when the system lands. */
export interface PendingEffect {
    id: string;
    system: string;
    /** Absent for an effect that belongs to the scene rather than to any one part. */
    partId: string | undefined;
    bone?: string;
    /** A trailing fire lasts as long as the debris; a blast plays once. */
    once: boolean;
    /** Where to put it when it belongs to no part. See `ParticlePlacement.at`. */
    at?: { x: number; y: number; z: number };
    /** The bone it rides, by index, where that is what the game names. See `ParticlePlacement.attachBoneIndex`. */
    boneIndex?: number;
}

/** What useParticleEffects reads from the rest of the preview. */
export interface ParticleEffectsInputs {
    assets: PreviewAssetLoader;
    refreshStats: () => void;
    setHiddenEmitters: Dispatch<SetStateAction<ReadonlySet<number>>>;
    viewportRef: RefObject<PreviewViewport | null>;
}

export type ParticleEffects = ReturnType<typeof useParticleEffects>;

export function useParticleEffects({
    assets,
    refreshStats,
    setHiddenEmitters,
    viewportRef,
}: ParticleEffectsInputs) {
    /**
     * Particle systems by name, and the attachments waiting on each.
     *
     * Refs, not state: they are mutated from the message handler as parts and systems arrive in
     * whatever order the server answers, and re-rendering on each arrival would buy nothing. Fetched
     * once per NAME - the loader sees to that, whoever asks - and instantiated once per entry: a
     * Star Destroyer has twenty proxies calling for p_hp_imperial_damage, and fetching that twenty
     * times is twenty round trips for one answer.
     */
    const systemsRef = useRef(new Map<string, AlamoParticleContent>());

    /**
     * Each system's owning object `Scale_Factor`, keyed like {@link systemsRef}.
     *
     * A uniform render scale on the whole system. Only a `<Particle>` GAME OBJECT can carry one - a
     * model's proxy names the asset directly and gets 1 - so it is keyed by the name that was
     * ASKED for, which is the object's where there is one.
     */
    const systemScalesRef = useRef(new Map<string, number>());
    const pendingRef = useRef<PreviewParticle[]>([]);

    /**
     * Systems the server could not read, by lower-cased name.
     *
     * A name is asked for once per scene, so nothing will answer for one of these again: an effect
     * started on it is dropped rather than queued for an answer that is not coming.
     */
    const missingSystemsRef = useRef(new Set<string>());

    /**
     * The effects belonging to a PASSIVE subject, by id.
     *
     * They answer to their own model rather than to the subject's damage rules, and this is how the
     * preview tells them apart when the subject's effects are dropped.
     */
    const passiveEffectIdsRef = useRef(new Set<string>());

    /**
     * Effects asked for but not yet delivered, to play the moment they arrive.
     *
     * The attachment travels with the request because the two callers want different ones: a
     * hardpoint's death explosion goes off at its bone on the hull, and a wreck's goes off at the
     * WRECK, which by then has drifted a long way from the hardpoint it left.
     */
    const explosionsRef = useRef<PendingEffect[]>([]);

    /**
     * Shows or hides one emitter.
     *
     * Held in the shell as well as the viewport because the checkbox has to survive a restart, which
     * rebuilds every instance from scratch.
     */
    const toggleEmitter = useCallback(
        (systemId: string, index: number, visible: boolean): void => {
            setHiddenEmitters((current) => {
                const next = new Set(current);
                if (visible) {
                    next.delete(index);
                } else {
                    next.add(index);
                }
                return next;
            });

            viewportRef.current?.setEmitterVisible(systemId, index, visible);
        },
        [],
    );

    /**
     * Whether one effect should be drawing right now.
     *
     * THE one answer, because there are two callers - the pass below and `attachPending`, which
     * runs later when a system's data finally lands. They disagreed once: `attachPending` set
     * visibility from the damage rules alone and clobbered the ability decision made moments
     * earlier, so an ability's proxy was drawing before anyone switched it on. Two writers of one
     * flag is the same defect the effect-row work already paid for.
     *
     * The proxy's own flag, for every effect: the subject's, a passive subject's, and a model
     * preview's. Everything the game's rules say about an effect - its name group, the abilities'
     * groups, the hardpoints' bones, the cloak - is a write of its state switch in the unit's rules,
     * over this answer - see `UnitRules.effectState`.
     */
    const effectDrawsNow = useCallback(
        (particle: PreviewParticle): boolean => effectPlaysNow(particle),
        [],
    );

    /**
     * Takes a passive subject's effects away with it.
     *
     * A wreck reaching the end of its lifetime and a clone removed by a repair both take their
     * geometry, and an effect left behind would go on burning at a part that no longer exists.
     */
    const dropPassiveEffects = useCallback((partId: string): void => {
        const mine = `${partId}#`;

        for (const id of [...passiveEffectIdsRef.current]) {
            if (!id.startsWith(mine)) {
                continue;
            }

            passiveEffectIdsRef.current.delete(id);
            viewportRef.current?.removeParticleSystem(id);
        }

        pendingRef.current = pendingRef.current.filter(
            (particle) => !particle.id.startsWith(mine),
        );
    }, []);

    /**
     * Takes one gameplay effect away, whether it is burning or still waiting for its system to
     * arrive - an effect asked for a moment before its owner went must not start after it.
     */
    const cancelEffect = useCallback((id: string): void => {
        explosionsRef.current = explosionsRef.current.filter(
            (pending) => pending.id !== id,
        );
        viewportRef.current?.removeParticleSystem(id, true);
    }, []);

    /** Attaches every pending particle whose system and part have both arrived. */
    const attachPending = useCallback((): void => {
        const viewport = viewportRef.current;
        if (viewport === null) {
            return;
        }

        const stillWaiting: PreviewParticle[] = [];

        for (const particle of pendingRef.current) {
            const system = systemsRef.current.get(
                particle.systemRef.toLowerCase(),
            );

            if (system === undefined || !viewport.hasPart(particle.partId)) {
                stillWaiting.push(particle);
                continue;
            }

            // The MODEL's own: it is in the scene's proxy list, it names a bone, and it is part of
            // what the asset is. This is the one call site that says so.
            viewport.addParticleSystem(particle.id, system, {
                origin: "model",
                attachToPartId: particle.partId,
                attachBone: particle.bone,
                attachBoneIndex: particle.boneIndex,
                proxyName: particle.systemRef,
                levels: {
                    alt: particle.alt ?? null,
                    lod: particle.lod ?? null,
                    altDecreaseStayHidden:
                        particle.altDecreaseStayHidden ?? false,
                },
                scaleFactor:
                    systemScalesRef.current.get(
                        particle.systemRef.toLowerCase(),
                    ) ?? 1,
            });

            viewport.setParticleSystemVisible(
                particle.id,
                effectState(particle.startsVisible, effectDrawsNow(particle)),
            );
        }

        pendingRef.current = stillWaiting;

        // Everything the dock reads, not just the tree. Systems attach well after the geometry
        // does, and a model can declare its damage states ENTIRELY on proxies - `Rv_mptl-2a.alo`
        // has eleven ALT-tagged smoke and static effects and not one ALT-tagged mesh. Refreshing
        // only the tree left `levels` at what the meshes alone implied, so the Damage state control
        // never appeared and ALT looked unimplemented on exactly the models that lean on it hardest.
        refreshStats();

        for (const name of viewport.particleTextureNames()) {
            assets.requestTexture(name);
        }
        // The effects too: a system names the engine effect it is drawn with, and the geometry
        // paths asked for shaders before any system existed. Without this a field opened on its
        // own never asks for its effect, and the preview's own shader stays - with nothing to say
        // so. Measured on the asteroid field, as "no difference" after two shader fixes.
        for (const shader of viewport.shaderNames()) {
            assets.requestShader(shader);
        }
    }, [refreshStats, effectDrawsNow, assets]);

    /**
     * Queues a passive subject's own effects, once its geometry is in the scene.
     *
     * Through the SAME pending queue every other effect uses, which is what stage 3 of the
     * multi-subject work bought: the descriptor is re-homed onto the part that was loaded and then
     * there is nothing special about it. A death clone's explosions and a burning wreck's fire
     * trail could not be shown at all before, because nothing on the wire described them.
     */
    const queuePassiveEffects = useCallback(
        (particles: readonly PreviewParticle[], partId: string): void => {
            if (particles.length === 0) {
                return;
            }

            const placed = passiveEffects(particles, partId);

            for (const particle of placed) {
                passiveEffectIdsRef.current.add(particle.id);
            }

            pendingRef.current = [...pendingRef.current, ...placed];

            // One request per distinct system, however many proxies call for it; the loader posts
            // a name once per scene.
            for (const name of new Set(
                placed.map((particle) => particle.systemRef),
            )) {
                assets.requestSystem(name);
            }

            // Attached here for the systems already in hand, which with a scene's effects fetched
            // up front is most of them: no answer is on its way to do it, and a wreck whose effects
            // were all the ship's own used to wait for an unrelated system to arrive.
            attachPending();
        },
        [assets, attachPending],
    );

    /**
     * Plays a named effect at an attachment, fetching it first if it is not in hand.
     *
     * The single route to a gameplay effect. Both callers used to have their own copy of the
     * have-it / ask-for-it dance and only one of them had the "play it when it lands" half, which
     * is why a wreck named its trailing fire and its death blast and showed neither.
     */
    const playEffect = useCallback(
        (
            id: string,
            name: string,
            partId: string | undefined,
            bone: string | undefined,
            once: boolean,
            /**
             * Where to put it when it hangs on no part.
             *
             * A part-less effect otherwise sits at the model root, which is right for a ship's own death
             * blast - it goes off where the ship was - and wrong for a wreck that flew away first.
             */
            at?: { x: number; y: number; z: number },
            /** The bone by index, for an effect the game adds on a bone it names by position - the root. */
            boneIndex?: number,
        ): void => {
            if (name === "") {
                return;
            }

            // Asked for and not there: nothing more is coming, so there is nothing to wait for.
            if (missingSystemsRef.current.has(name.toLowerCase())) {
                return;
            }

            const system = systemsRef.current.get(name.toLowerCase());

            if (system === undefined) {
                // Played on arrival. A scene's effects are asked for when the scene arrives - see
                // `prefetchEffects` - so this is an effect started before its answer came back; the
                // loader posts the name only if nothing has asked for it yet.
                explosionsRef.current.push({
                    id,
                    system: name,
                    partId,
                    bone,
                    once,
                    at,
                    boneIndex,
                });
                assets.requestSystem(name);
                return;
            }

            const viewport = viewportRef.current;

            const scale = systemScalesRef.current.get(name.toLowerCase()) ?? 1;

            if (once) {
                viewport?.playOnce(id, system, partId, bone, scale, at);
            } else {
                // The Gameplay lens's: asked for BY NAME at the moment it is needed - a wreck's
                // trailing fire - so it is in no proxy list and belongs to no bone. Out of the model
                // tree, which is a list of what the asset is made of.
                viewport?.addParticleSystem(id, system, {
                    origin: "gameplay",
                    attachToPartId: partId,
                    attachBone: bone,
                    attachBoneIndex: boneIndex,
                    scaleFactor: scale,
                    at,
                });
            }
        },
        [assets],
    );

    /**
     * Asks for every effect the scene can start by name, before any of them is started.
     *
     * Nothing is put in the scene for them: a system that arrives with nothing waiting on it is
     * kept, its textures and effects are fetched, and it plays the moment something starts it.
     */
    const prefetchEffects = useCallback(
        (scene: SceneEffects): void => {
            for (const name of sceneEffectNames(scene)) {
                assets.requestSystem(name);
            }
        },
        [assets],
    );

    /**
     * Takes a system that has arrived: keeps it by name and starts whatever was waiting on it.
     *
     * Only what was waiting. A system nothing has started yet stays data - an explosion fetched
     * up front must not go off because it landed.
     */
    const takeSystem = useCallback(
        (name: string, system: AlamoParticleContent, scale: number): void => {
            const key = name.toLowerCase();
            const viewport = viewportRef.current;

            systemsRef.current.set(key, system);
            systemScalesRef.current.set(key, scale);
            missingSystemsRef.current.delete(key);

            // An effect asked for before its system arrived plays as soon as it lands.
            const waiting = explosionsRef.current.filter(
                (pending) => pending.system.toLowerCase() === key,
            );

            if (waiting.length > 0) {
                explosionsRef.current = explosionsRef.current.filter(
                    (pending) => !waiting.includes(pending),
                );

                // The scale that arrived with THIS system, not a lookup: an effect asked for before
                // its system landed takes the same route as one asked for after, and the two
                // disagreeing is exactly how a wreck's fire came back unscaled.
                for (const pending of waiting) {
                    if (pending.once) {
                        viewport?.playOnce(
                            pending.id,
                            system,
                            pending.partId,
                            pending.bone,
                            scale,
                            pending.at,
                        );
                    } else {
                        viewport?.addParticleSystem(pending.id, system, {
                            origin: "gameplay",
                            attachToPartId: pending.partId,
                            attachBone: pending.bone,
                            attachBoneIndex: pending.boneIndex,
                            scaleFactor: scale,
                            at: pending.at,
                        });
                    }
                }
            }

            attachPending();
        },
        [attachPending],
    );

    /** A system came back unread: whatever was waiting on it is dropped, and nothing waits again. */
    const systemMissing = useCallback((name: string): void => {
        const key = name.toLowerCase();

        missingSystemsRef.current.add(key);
        explosionsRef.current = explosionsRef.current.filter(
            (pending) => pending.system.toLowerCase() !== key,
        );
    }, []);

    /**
     * Forgets every system and every effect still waiting on one, for a new scene.
     *
     * What is waiting names parts of the scene before. Its system is asked for again with the new
     * scene, and an effect left queued would go off on arrival - on a unit that has only just opened.
     */
    const forgetSystems = useCallback((): void => {
        systemsRef.current.clear();
        systemScalesRef.current.clear();
        missingSystemsRef.current.clear();
        explosionsRef.current = [];
    }, []);

    return {
        pendingRef,
        passiveEffectIdsRef,
        toggleEmitter,
        effectDrawsNow,
        queuePassiveEffects,
        dropPassiveEffects,
        cancelEffect,
        attachPending,
        playEffect,
        prefetchEffects,
        takeSystem,
        systemMissing,
        forgetSystems,
    };
}
