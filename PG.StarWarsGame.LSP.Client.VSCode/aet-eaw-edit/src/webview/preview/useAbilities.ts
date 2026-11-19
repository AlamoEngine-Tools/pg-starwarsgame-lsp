// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The abilities: which the subject has, which the reader has switched on, and the proxies and effects
// each one holds off or swaps in. One hook so the command bar, its flyouts and the combat lens read one
// object.

import {
    useCallback,
    useMemo,
    useRef,
    useState,
    type Dispatch,
    type SetStateAction,
    type RefObject,
} from "react";
import { type PreviewScene } from "../../protocol/modelPreview";
import { type PreviewViewport } from "./viewport";
import { replacedByAbility } from "./particleScene";
import {
    abilityOwnership,
    abilityProxies,
    abilityRows,
    clipFor,
    unboundEffectIds,
} from "./abilityRows";

/** What useAbilities reads from the rest of the preview. */
export interface AbilitiesInputs {
    scene: PreviewScene | null;
    setAnimation: Dispatch<SetStateAction<string | null>>;
    viewportRef: RefObject<PreviewViewport | null>;
}

export type Abilities = ReturnType<typeof useAbilities>;

export function useAbilities({
    scene,
    setAnimation,
    viewportRef,
}: AbilitiesInputs) {
    /**
     * The abilities the reader has switched on.
     *
     * Tier 2 - it describes what you are looking at right now, so it resets with the subject. The
     * proxies it holds off ride the ordinary effect gate, which means a held burst does NOT burn
     * its life away and come back exhausted.
     */
    const [activeAbilities, setActiveAbilities] = useState<ReadonlySet<string>>(
        new Set(),
    );

    /**
     * Same reason again, for the ability decider.
     *
     * A system attaching after an ability was switched on has to see the CURRENT set, or it lands
     * with the visibility the scene had when the handler was made.
     */
    const abilityProxyIdsRef = useRef<ReadonlyMap<string, string[]>>(new Map());

    const activeAbilitiesRef = useRef<ReadonlySet<string>>(new Set());

    /**
     * Effects this unit can never show. Read through a ref for the same reason as the two above:
     * a system attaches long after the scene lands, and it has to meet the current answer.
     */
    const unboundEffectsRef = useRef<ReadonlySet<string>>(new Set());
    /** What an active ability is standing in for. See {@link replacedByAbility}. */
    const replacedEffectsRef = useRef<ReadonlySet<string>>(new Set());

    /**
     * Which particle ids each ability drives.
     *
     * Resolved from proxy BONE NAMES to ids here, once: the server binds by name, and a name is not
     * an identity - a Star Destroyer carries twenty proxies sharing one.
     */
    const abilityProxyIds = useMemo(
        () => abilityProxies(scene?.abilities ?? [], scene?.particles ?? []),
        [scene?.abilities, scene?.particles],
    );

    const abilities = useMemo(
        () => abilityRows(scene?.abilities ?? [], abilityProxyIds),
        [scene?.abilities, abilityProxyIds],
    );

    /**
     * The effects whose ability this unit never declares, which nothing can switch on.
     *
     * Not the complement of {@link abilityProxyIds}: that map holds only the BOUND proxies, so an
     * unbound one is simply absent from it and absence is what let it fall through to the ordinary
     * path and light up.
     */
    const unboundEffects = useMemo(
        () => unboundEffectIds(scene?.abilities ?? [], scene?.particles ?? []),
        [scene?.abilities, scene?.particles],
    );

    /**
     * The effects an ACTIVE ability replaces - turbo engines standing in for the plain ones.
     *
     * Recomputed with the active set rather than held, because it is a statement about right now.
     */
    const replacedEffects = useMemo(
        () =>
            replacedByAbility(
                scene?.particles ?? [],
                abilityProxyIds,
                activeAbilities,
            ),
        [scene?.particles, abilityProxyIds, activeAbilities],
    );

    // Kept in step for the decider, which the message handler reads through refs.
    abilityProxyIdsRef.current = abilityProxyIds;
    activeAbilitiesRef.current = activeAbilities;
    unboundEffectsRef.current = unboundEffects;
    replacedEffectsRef.current = replacedEffects;

    /**
     * Switches one ability on or off.
     *
     * Its proxies follow through the effect pass above; the CLIP is fired here, because it is an
     * event rather than a state - replaying the deploy on every re-render would leave a model
     * permanently deploying, which is the same shape as the death-explosion rule beside it.
     */
    const setAbilityActive = useCallback(
        (type: string, on: boolean): void => {
            // Before the state changes, so the chain re-runs once with both the override gone and the
            // new gate in place.
            //
            // Every channel an ability drives - its proxies, the shield mesh, the stealth shell - is a
            // write of the state switch, which the reader's word sits above. So one tick on such
            // a row in Model mode outranked this switch for good: it fired, the chain ignored it, and
            // only a whole-model Reset could undo it. Switching the ability takes ITS OWN rows back,
            // which is the rule a starting clip already follows - scoped, because a mesh hidden for an
            // unrelated reason has to survive it.
            viewportRef.current?.releaseAbilityRows(
                abilityOwnership(type, abilityProxyIdsRef.current),
            );

            setActiveAbilities((current) => {
                if (current.has(type) === on) {
                    return current;
                }

                const next = new Set(current);
                if (on) {
                    next.add(type);
                } else {
                    next.delete(type);
                }

                return next;
            });

            const declared = scene?.abilities?.find((a) => a.type === type);
            const clip = declared === undefined ? null : clipFor(declared, on);

            // 25 models ship a deploy and only 23 the matching undeploy, so a missing clip is ordinary:
            // the ability still switches, it simply has no animation for that direction.
            if (clip !== null) {
                setAnimation(clip);
            }
        },
        [scene],
    );

    return {
        activeAbilities,
        setActiveAbilities,
        abilityProxyIdsRef,
        activeAbilitiesRef,
        unboundEffectsRef,
        replacedEffectsRef,
        abilityProxyIds,
        abilities,
        setAbilityActive,
    };
}
