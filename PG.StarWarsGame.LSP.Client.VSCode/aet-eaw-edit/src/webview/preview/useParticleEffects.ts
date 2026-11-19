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
import { post } from "./previewPost";
import { type PreviewViewport } from "./viewport";
import { type PreviewAssetLoader } from "./previewAssetLoader";
import { effectPlaysNow } from "./damage";
import { passiveEffectPlaysNow, passiveEffects } from "./passiveSubject";
import { abilityAllows, abilityClaims } from "./abilityRows";
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
}

/** What useParticleEffects reads from the rest of the preview. */
export interface ParticleEffectsInputs {
    abilityProxyIdsRef: RefObject<ReadonlyMap<string, string[]>>;
    activeAbilitiesRef: RefObject<ReadonlySet<string>>;
    assets: PreviewAssetLoader;
    refreshStats: () => void;
    replacedEffectsRef: RefObject<ReadonlySet<string>>;
    setHiddenEmitters: Dispatch<SetStateAction<ReadonlySet<number>>>;
    unboundEffectsRef: RefObject<ReadonlySet<string>>;
    viewportRef: RefObject<PreviewViewport | null>;
}

export type ParticleEffects = ReturnType<typeof useParticleEffects>;

export function useParticleEffects({
    abilityProxyIdsRef,
    activeAbilitiesRef,
    assets,
    refreshStats,
    replacedEffectsRef,
    setHiddenEmitters,
    unboundEffectsRef,
    viewportRef,
}: ParticleEffectsInputs) {
    /**
     * Particle systems by name, and the attachments waiting on each.
     *
     * Refs, not state: they are mutated from the message handler as parts and systems arrive in
     * whatever order the server answers, and re-rendering on each arrival would buy nothing. Fetched
     * once per NAME and instantiated once per entry - a Star Destroyer has twenty proxies calling for
     * p_hp_imperial_damage, and fetching that twenty times is twenty round trips for one answer.
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
    const requestedSystemsRef = useRef(new Set<string>());

    /**
     * The effects belonging to a PASSIVE subject, by id.
     *
     * They answer to their own model rather than to the subject's opening and damage rules - see
     * `passiveEffectPlaysNow` - and this is how the one place that decides whether an effect draws
     * tells them apart.
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
     */
    const effectDrawsNow = useCallback((particle: PreviewParticle): boolean => {
        const proxies = abilityProxyIdsRef.current;

        // A passive subject's effect answers to its own model and to nothing here. Neither of the
        // rules below is about it: the opening rule is about the first moment of a preview, and a
        // death clone has none - it exists because the ship died, and its explosions ARE the death.
        //
        // And so does every effect of a MODEL preview, which shows the model file as it is: no
        // hardpoint, ability or opening rule of the game applies to it. The rules below are the
        // unit preview's, written over that same file answer.
        if (
            passiveEffectIdsRef.current.has(particle.id) ||
            viewportRef.current?.appliesGameRules() !== true
        ) {
            return passiveEffectPlaysNow(particle);
        }

        // Before every other rule, because it is the only VETO among them: an effect whose name
        // claims an ability this unit does not declare has nothing that could ever switch it on,
        // and the game simply never shows it. Everything below is a DEFAULT or a gate - the
        // distinction three separate defects were spent learning. `Tartan_Patrol_Cruiser` opened
        // with its TURBO engines burning because an unbound proxy fell through to the ordinary
        // path, where an engine-named effect is exempt from the quiet-on-open rule.
        if (unboundEffectsRef.current.has(particle.id)) {
            return false;
        }

        // Neither the cloak nor a hardpoint's bones are decided here. Both are writes of the
        // effect's state switch in the unit's rules, after this answer - see `UnitRules`: the
        // cloak hides every effect, a Damage_Particles bone hides its smoke until the hardpoint
        // dies, and an Engine_Particles bone hides its glow once it does.

        // An ability REPLACES the quiet-on-open rule for the proxies it claims, rather than being
        // ANDed with it. The opening rules hold every non-engine effect back - rightly, on open -
        // but they are about the first moment, not a permanent veto, and ANDing them left an
        // activated ability changing nothing at all. The hardpoint bone rules still apply either way.
        if (abilityClaims(particle.id, proxies)) {
            return abilityAllows(
                particle.id,
                proxies,
                activeAbilitiesRef.current,
            );
        }

        // An active ability REPLACING this effect's family, which is a different statement from
        // claiming it - see `replacedByAbility`. Below the claim branch on purpose: a proxy an
        // ability actually drives answers to that ability, not to this.
        if (replacedEffectsRef.current.has(particle.id)) {
            return false;
        }

        return effectPlaysNow(particle);
    }, []);

    /**
     * Queues a passive subject's own effects, once its geometry is on the way.
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

            // One request per distinct system, however many proxies call for it - and a wreck's
            // are usually the ship's own, so most come back off the cache immediately.
            for (const name of new Set(
                placed.map((particle) => particle.systemRef),
            )) {
                if (requestedSystemsRef.current.has(name.toLowerCase())) {
                    continue;
                }

                requestedSystemsRef.current.add(name.toLowerCase());
                post({ type: "requestParticleSystem", name });
            }
        },
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
        ): void => {
            if (name === "") {
                return;
            }

            const system = systemsRef.current.get(name.toLowerCase());

            if (system === undefined) {
                // Fetched now and played on arrival; an effect nothing referenced until this moment is
                // not worth loading up front for every hardpoint on a capital ship.
                explosionsRef.current.push({
                    id,
                    system: name,
                    partId,
                    bone,
                    once,
                    at,
                });
                post({ type: "requestParticleSystem", name });
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
                    scaleFactor: scale,
                    at,
                });
            }
        },
        [],
    );

    return {
        systemsRef,
        systemScalesRef,
        pendingRef,
        requestedSystemsRef,
        passiveEffectIdsRef,
        explosionsRef,
        toggleEmitter,
        effectDrawsNow,
        queuePassiveEffects,
        dropPassiveEffects,
        attachPending,
        playEffect,
    };
}
