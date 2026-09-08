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
    /**
     * What the row is called: the command bar's own words when they resolve, the type otherwise.
     *
     * NOT `guiName` - that names a `SpecialAbility` block and is an identifier, not display text.
     * Labelling with it put `Corvette_Turbo_Ability` where the reader expects "Turbo Boost", and
     * since 39% of shipped abilities carry the tag, that was the common case rather than the edge.
     */
    label: string;
    /** The tooltip the game shows, when the project ships the text. */
    description: string | null;
    /**
     * The command-bar icon, ready to draw. Null means nothing could be asserted about it - draw no
     * slot rather than a placeholder, since the server already sends the placeholder (and a
     * problem) for an icon that IS named and missing.
     */
    iconDataUri: string | null;
    /**
     * The `SpecialAbility` this ability names, for a jump to where it is defined. Null when it
     * names none, which is most of them - the row simply offers nothing to click then.
     */
    definition: string | null;
    detail: string;
    proxyCount: number;
    /**
     * The `SpecialAbility` block this names - an IDENTIFIER, never display text.
     *
     * Carried so the info flyout can show it. It is on 39% of shipped abilities and it is what a
     * jump resolves; putting it where a name belongs is what once wrote `Corvette_Turbo_Ability`
     * over a button the reader expected to say "Turbo Boost".
     */
    guiName: string | null;
    rechargeSeconds: number | null;
    expirationSeconds: number | null;
    particleEffect: string | null;
    ownerAttachmentBone: string | null;
    modifiers: readonly { stat: string; factor: number }[];
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
 * The damage rules still apply on top - a proxy gated to a destroyed hardpoint does not come back just
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
 * Effects the unit can never show, because it does not declare the ability their name claims.
 *
 * The game hides these; it simply never has anything to switch them on. We drew them, and on an
 * engine-named proxy the quiet-on-open rule lets an effect play from the first frame - so
 * `Tartan_Patrol_Cruiser` opened with its TURBO engines burning for a TURBO it does not declare.
 *
 * This is a VETO, not a default: no state, no reader tick and no ability can turn one of these on.
 * That makes it the one rule in the chain that comes first. Contrast {@link abilityAllows}, which
 * is about a bound effect waiting for its ability - a different question with a different answer.
 *
 * Rare by design: 39 proxies over both shipped trees carry a prefix at all, against 5404 that do
 * not, and a proxy claiming nothing is never unbound.
 */
export function unboundEffectIds(
    abilities: readonly PreviewAbility[], particles: readonly PreviewParticle[],
): Set<string> {
    // Case-insensitive both ways: `Ev_acclamator` writes `power_to_weapons` in lower case, and the
    // proxy names are written both ways too - `pptw_2mtank` beside `PTE_Corvetteengines`.
    const declared = new Set(abilities.map(a => a.type.trim().toUpperCase()));
    const unbound = new Set<string>();

    for (const particle of particles) {
        const claim = particle.claimsAbility?.trim().toUpperCase();

        if (claim !== undefined && claim !== '' && !declared.has(claim)) {
            unbound.add(particle.id);
        }
    }

    return unbound;
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

/**
 * Ability types that CLOAK the unit: the stealth shell is drawn and nothing else is.
 *
 * A table rather than a name test, and the two members do not share a word - which is the whole
 * argument for the table. `TIE_Phantom`, `Vengeance_Frigate`, `Tyber_Zann` and `Urai_Fen` declare
 * STEALTH; `Luke_Skywalker_Jedi` declares FORCE_CLOAK and carries the same `stealth` mesh. Reading
 * the mesh's own name for the ability would have left Luke's shell dead.
 *
 * Unlike the shield, the shell ships VISIBLE - every one of the nine does - so a model with one was
 * drawing its cloak over its hull from the first frame, permanently, which is what the reader saw
 * on the Vengeance. See {@link isStealthMesh}.
 */
const STEALTH_ABILITIES = new Set(['STEALTH', 'FORCE_CLOAK']);

/** Whether this ability type cloaks the unit. */
export function revealsStealth(abilityType: string): boolean {
    return STEALTH_ABILITIES.has(abilityType.trim().toUpperCase());
}

/**
 * Whether the unit is cloaked right now, and so drawing nothing but its stealth shell.
 *
 * False is the permanent answer for a unit that declares no cloak at all, which is what keeps the
 * shell off `Tyber_Zann_Prisoner` - he uses Tyber's model and declares no abilities - and off
 * `W_mousedroid`, a prop no unit declares anything for.
 */
export function stealthed(active: ReadonlySet<string>): boolean {
    for (const type of active) {
        if (revealsStealth(type)) {
            return true;
        }
    }

    return false;
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

/**
 * The rows one ability speaks for.
 *
 * Every channel an ability drives enters the visibility chain at the BOTTOM of it, as the model's
 * own word: a proxy's `gateVisible` and the shield and shell meshes' `inFile` are all read by the
 * `file` link. The reader sits four links above that. So a single tick on one of these rows outranks
 * the ability permanently - the switch still fires, the chain still ignores it, and there is no way
 * back short of resetting the whole model. That is the defect this exists to fix.
 *
 * The answer is the same one a clip already gets: switching an ability TAKES ITS OWN ROWS BACK, the
 * way starting a clip takes the whole model back (`clearedForPlayback`). A clip may be global
 * because it resets the pose; an ability may not, because it is one statement about a handful of
 * rows and the reader's word on everything else still stands.
 */
export interface AbilityOwnership {
    /** The particle systems this ability drives. */
    systemIds: readonly string[];
    /** Whether it speaks for the model's shield mesh. */
    shieldMesh: boolean;
    /** Whether it speaks for the model's stealth shell. */
    stealthShell: boolean;
}

/** What switching one ability takes back from the reader. */
export function abilityOwnership(
    type: string, proxies: ReadonlyMap<string, string[]>,
): AbilityOwnership {
    return {
        systemIds: proxies.get(type) ?? [],
        shieldMesh: revealsShield(type),
        stealthShell: revealsStealth(type),
    };
}

/** The clip to play for a change of state, or null when the model ships none for that direction. */
export function clipFor(ability: PreviewAbility, activating: boolean): string | null {
    const declared = (activating ? ability.deployClip : ability.undeployClip) ?? '';

    // The XML names a FILE; the mixer holds the clip under its STEM. Measured on the X-Wing: the
    // ability declares `rv_xwing_deploy_00.ala` and the loaded glTF animation is called
    // `rv_xwing_deploy_00`, so `Viewport.play` looked the clip up by a name no clip has and found
    // nothing - silently, which is why the panel went on reporting one as playing.
    //
    // Only an `.ala` comes off, rather than everything after the last dot: nothing guarantees a mod
    // writes the extension at all, and a name that merely contains a dot must survive intact.
    const stem = declared.replace(/\.ala$/i, '').trim();

    return stem === '' ? null : stem;
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
            || revealsStealth(ability.type)
            || (ability.ownerAttachmentBone ?? '') !== ''
            || (ability.particleEffect ?? '') !== '';

        const definition = (ability.guiName ?? '').trim();

        return {
            type: ability.type,
            label: (ability.name ?? '').trim() === '' ? ability.type : ability.name!.trim(),
            description: (ability.description ?? '').trim() === '' ? null : ability.description!,
            iconDataUri: (ability.iconDataUri ?? '') === '' ? null : ability.iconDataUri!,
            definition: definition === '' ? null : definition,
            detail: detailOf(ability, proxyCount, deployClip, undeployClip),
            guiName: (ability.guiName ?? '') === '' ? null : ability.guiName!,
            rechargeSeconds: ability.rechargeSeconds ?? null,
            expirationSeconds: ability.expirationSeconds ?? null,
            particleEffect: (ability.particleEffect ?? '') === '' ? null : ability.particleEffect!,
            ownerAttachmentBone: (ability.ownerAttachmentBone ?? '') === ''
                ? null
                : ability.ownerAttachmentBone!,
            modifiers: ability.modifiers ?? [],
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

    // ONLY, because that is the whole of what a cloak draws - the hull, the effects and the
    // shadow volumes all go with it. Said on the row because the ability declares nothing else:
    // `TIE_Phantom`'s STEALTH is a type and an icon, so without this the row read
    // "no effects, bones or clips declared" beside a key that cloaks the ship.
    if (revealsStealth(ability.type)) {
        parts.push('shows the stealth mesh only');
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

/**
 * What the row's Definition button promises, in both of its states.
 *
 * The button stays on every row and goes disabled where the ability names nothing, so the title is
 * the whole of the explanation for why it will not act. Naming the TYPE in that case matters: the
 * reader is looking at a list of twenty rows and needs to know which one is being talked about.
 */
export function gotoDefinitionTitle(row: AbilityRow): string {
    return row.definition === null
        ? `${row.type} names no ability block, so there is nothing to open`
        : `Open where ${row.definition} is defined`;
}

/**
 * The whole of what an ability key on the command bar can say about itself.
 *
 * The bar draws an ICON and nothing else - that is what makes it a command bar rather than a list -
 * so the tooltip is the only place the name, the tooltip text and the effect tally appear. It is
 * three lines rather than a sentence because they answer three different questions: what is this,
 * what does the game say it does, and what will pressing it show me.
 *
 * A key that drives nothing is DISABLED and gets a fourth line saying why: {@link AbilityRow.title}
 * carries "drives nothing on this model - it is an order, not an effect". A dead control that
 * cannot explain itself is worse than no control. A key that does drive something needs no such
 * line: its detail already says what, and pressing it shows you.
 */
export function abilityBarTitle(row: AbilityRow): string {
    const head = row.label === row.type ? row.type : `${row.label} (${row.type})`;
    const lines = [head];

    if (row.description !== null) {
        lines.push(row.description);
    }

    // The TALLY, not the action wording - `title` says "Show what X drives", which the icon being
    // pressable already says, while `detail` is the answer: "2 effects, 60s recharge".
    lines.push(row.detail);

    if (!row.drivesSomething) {
        lines.push(row.title);
    }

    return lines.join('\n');
}

/**
 * What the FILE says about an ability, as labelled rows for its info flyout.
 *
 * The card's face carries what the GAME tells a player - the icon, the localised name and its
 * description. Everything here is the other half: the numbers and the names a modder checks against
 * their own XML. They used to run together into one paragraph under the description, so the
 * reader's own prose and the measurements had no seam between them.
 *
 * Pairs, so the order is part of it, and nothing the file does not declare.
 */
export function abilityFacts(row: AbilityRow): [string, string][] {
    const seconds = (value: number | null): string | null =>
        value === null ? null : `${value}s`;

    const rows: [string, string | null][] = [
        ['Recharge', seconds(row.rechargeSeconds)],
        ['Lasts', seconds(row.expirationSeconds)],
        ['Defined by', row.guiName],
        ['Effect', row.particleEffect],
        ['On bone', row.ownerAttachmentBone],
        ['Effects driven', row.proxyCount === 0 ? null : String(row.proxyCount)],
        ['Deploy clip', row.deployClip],
        ['Undeploy clip', row.undeployClip],
        // As the game writes them - `speed x0.8` - so a reader can match the line against the XML
        // without translating it first.
        ['Modifiers', row.modifiers.length === 0
            ? null
            : row.modifiers.map(m => `${m.stat.toLowerCase()} x${m.factor}`).join(', ')],
    ];

    return rows.filter((entry): entry is [string, string] =>
        entry[1] !== null && entry[1] !== undefined && entry[1] !== '');
}
