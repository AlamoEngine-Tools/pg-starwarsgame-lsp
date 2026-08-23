// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What a unit can DO, and what switching it on shows.
//
// An ability is a row you can activate: its bound particle proxies come on and its deploy clip
// plays; switching it off plays the undeploy, when the model ships one.
//
// Holding a proxy off is the same mechanism as any other hidden effect, deliberately - the emission
// gate resolves `emitting && root.visible` at one point of use, so a held burst does NOT burn its
// life away unseen and then come back exhausted. That defect cost a whole session to find once; see
// `project_preview_effect_rows`. Nothing here needs to know about it beyond routing visibility
// through the same call every other effect uses.

import type { PreviewAbility, PreviewParticle } from '../../protocol/modelPreview';

/** One row in the Gameplay dock's Abilities section. */
export interface AbilityRow {
    /** The `Type`, which is what binds a proxy and what a modder searches for. */
    type: string;
    /** What the row is called: the GUI name when there is one, the type otherwise. */
    label: string;
    detail: string;
    proxyCount: number;
    deployClip: string | null;
    undeployClip: string | null;
    /**
     * Whether activating this would change anything VISIBLE, and so whether it gets a switch.
     *
     * A row without one is not a lesser row. DEFEND changes weapon delay, shield regen, energy
     * regen and speed and shows nothing at all - so it gets its modifiers spelled out instead of a
     * dead control. A disabled switch was worse than useless there: it implied the ability was
     * broken rather than invisible.
     */
    drivesSomething: boolean;
    title: string;
}

/**
 * Which particle ids each ability drives.
 *
 * Keyed by ability TYPE and valued by particle ID, because the server binds by proxy BONE NAME and
 * a bone name is not an identity - a Star Destroyer carries twenty proxies of one name. Resolving
 * to ids here is what stops nineteen of twenty engines staying dark.
 */
export function abilityProxies(
    abilities: readonly PreviewAbility[], particles: readonly PreviewParticle[],
): Map<string, string[]> {
    const byAbility = new Map<string, string[]>();

    for (const ability of abilities) {
        if (ability.proxyNames.length === 0) {
            continue;
        }

        const wanted = new Set(ability.proxyNames.map(name => name.toLowerCase()));
        const ids = particles
            .filter(particle => wanted.has(particle.bone.toLowerCase()))
            .map(particle => particle.id);

        if (ids.length > 0) {
            byAbility.set(ability.type, ids);
        }
    }

    return byAbility;
}

/**
 * Whether an ability lets this proxy draw right now.
 *
 * A DECIDER layered over the ordinary effect rules, not a replacement for them: a particle no
 * ability claims is left entirely alone, or activating one ability would silence the whole model.
 * A proxy two abilities claim plays as soon as either is active.
 */
export function abilityAllows(
    particleId: string,
    proxies: ReadonlyMap<string, string[]>,
    active: ReadonlySet<string>,
): boolean {
    let claimed = false;

    for (const [type, ids] of proxies) {
        if (!ids.includes(particleId)) {
            continue;
        }

        if (active.has(type)) {
            return true;
        }

        claimed = true;
    }

    return !claimed;
}

/**
 * Whether ANY ability claims this proxy.
 *
 * The caller needs this because an ability does not merely gate its proxy - it REPLACES the
 * quiet-on-open rule for it. Those opening rules hold every non-engine effect back, correctly, but
 * they are about the first moment rather than a permanent veto; ANDing them with the ability left
 * an activated ability changing nothing at all. Found on the live AT-AA, where MISSILE_SHIELD's
 * `prs_at-aa_fx` stayed dark however many times its row was ticked.
 *
 * The damage rules still apply on top - a proxy gated to a destroyed mount does not come back just
 * because an ability is on.
 */
export function abilityClaims(
    particleId: string, proxies: ReadonlyMap<string, string[]>,
): boolean {
    for (const ids of proxies.values()) {
        if (ids.includes(particleId)) {
            return true;
        }
    }

    return false;
}

/**
 * Ability types that reveal the model's SHIELD MESH.
 *
 * A table rather than a name test, deliberately: `MISSILE_SHIELD` and `SHIELD_FLARE` carry the word
 * and are different abilities, so guessing from it would light the mesh for the wrong ones.
 *
 * The mesh itself ships hidden - `MeshShield.fx` on `Ev_stardestroyer.alo` carries
 * `alamoHidden: true` - which is why the shield is invisible until something asks for it. Missing
 * this channel is what made DEFEND look like a dead switch: it declares no proxy, no bone, no
 * particle and no clip, and revealing the shield is the whole of what it shows.
 */
const SHIELD_ABILITIES = new Set(['DEFEND']);

/** Whether this ability type reveals the shield mesh. */
export function revealsShield(abilityType: string): boolean {
    return SHIELD_ABILITIES.has(abilityType.trim().toUpperCase());
}

/** Whether any active ability is currently holding the shield mesh visible. */
export function shieldRevealed(active: ReadonlySet<string>): boolean {
    for (const type of active) {
        if (revealsShield(type)) {
            return true;
        }
    }

    return false;
}

/** The clip to play for a change of state, or null when the model ships none for that direction. */
export function clipFor(ability: PreviewAbility, activating: boolean): string | null {
    return (activating ? ability.deployClip : ability.undeployClip) ?? null;
}

/** A row per declared ability, in document order. */
export function abilityRows(
    abilities: readonly PreviewAbility[], proxies: ReadonlyMap<string, string[]>,
): AbilityRow[] {
    return abilities.map(ability => {
        const proxyCount = proxies.get(ability.type)?.length ?? 0;
        const deployClip = ability.deployClip ?? null;
        const undeployClip = ability.undeployClip ?? null;

        const drivesSomething = proxyCount > 0
            || deployClip !== null
            || revealsShield(ability.type)
            || (ability.ownerAttachmentBone ?? '') !== ''
            || (ability.particleEffect ?? '') !== '';

        return {
            type: ability.type,
            label: (ability.guiName ?? '') === '' ? ability.type : ability.guiName!,
            detail: detailOf(ability, proxyCount, deployClip, undeployClip),
            proxyCount,
            deployClip,
            undeployClip,
            drivesSomething,
            title: drivesSomething
                ? `Show what ${ability.type} drives on this model`
                : `${ability.type} drives nothing on this model - it is an order, not an effect`,
        };
    });
}

function detailOf(
    ability: PreviewAbility,
    proxyCount: number,
    deployClip: string | null,
    undeployClip: string | null,
): string {
    const parts: string[] = [];

    if (proxyCount > 0) {
        parts.push(`${proxyCount} effect${proxyCount === 1 ? '' : 's'}`);
    }

    if ((ability.particleEffect ?? '') !== '') {
        parts.push(ability.particleEffect!);
    }

    if ((ability.ownerAttachmentBone ?? '') !== '') {
        parts.push(`on ${ability.ownerAttachmentBone}`);
    }

    if (revealsShield(ability.type)) {
        parts.push('shows the shield mesh');
    }

    if (deployClip !== null) {
        // Named rather than counted: which clip plays is the thing a reader is checking.
        parts.push(undeployClip === null ? 'deploy only' : 'deploy and undeploy');
    }

    // What a stat-only ability actually does. Written as the game writes it - `speed x0.8` - so a
    // reader can match it against the XML without translating.
    for (const modifier of ability.modifiers ?? []) {
        parts.push(`${statLabel(modifier.stat)} x${num(modifier.factor)}`);
    }

    if (ability.rechargeSeconds !== null && ability.rechargeSeconds !== undefined) {
        parts.push(`${num(ability.rechargeSeconds)}s recharge`);
    }

    if (ability.expirationSeconds !== null && ability.expirationSeconds !== undefined) {
        parts.push(`lasts ${num(ability.expirationSeconds)}s`);
    }

    return parts.length === 0 ? 'no effects, bones or clips declared' : parts.join(' - ');
}

/**
 * `SPEED_MULTIPLIER` as a person reads it.
 *
 * The suffix carries no information once the value is written as `x0.8`, and dropping it is what
 * lets four modifiers fit on a row instead of two.
 */
function statLabel(stat: string): string {
    return stat.replace(/_MULTIPLIER$/i, '').toLowerCase().replaceAll('_', ' ');
}

/** A number as a person writes it: the XML's `60.0000` is `60`. */
function num(value: number): string {
    return Number.parseFloat(value.toFixed(4)).toString();
}
