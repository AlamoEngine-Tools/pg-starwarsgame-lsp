// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import type { PreviewHardpoint, PreviewParticle } from '../../protocol/modelPreview';
import { PREVIEW_PARTICLE_GATE } from '../../protocol/modelPreview';
import {
    collisionMeshNames, decalNames, destroyable, effectPlays,
    hardpointGateAllows, partHidden, playsOnOpen,
} from './damage';

function hardpoint(over: Partial<PreviewHardpoint> = {}): PreviewHardpoint {
    return {
        id: 'HP_Weapon',
        partId: 'hp:HP_Weapon',
        type: null,
        attachBone: 'HP_F-L_Bone',
        isDestroyable: true,
        isTargetable: true,
        health: 325,
        damageParticlesBone: 'HP_F-L_EmitDamage',
        damageDecalBone: 'HP_F-L_Blast',
        collisionMeshBone: null,
        engineParticlesBone: null,
        deathExplosionParticles: 'Large_Explosion_Space_Empire',
        deathBreakoffProp: null,
        engineDeathHidesEngineParticles: false,
        tooltipText: null,
        turret: null,
        ...over,
    };
}

function particle(over: Partial<PreviewParticle> = {}): PreviewParticle {
    return {
        id: 'hull#0',
        systemRef: 'p_hp_imperial_damage',
        partId: 'hull',
        bone: 'p_hp_imperial_damage',
        boneIndex: 2,
        gate: 'Always',
        hardpointId: null,
        startsVisible: true,
        ...over,
    };
}

describe('partHidden', () => {
    it('breaks the turret off when its hardpoint is destroyed', () => {
        assert.equal(partHidden('hp:HP_Weapon', [hardpoint()], new Set(['HP_Weapon'])), true);
    });

    it('leaves an intact hardpoint mounted', () => {
        assert.equal(partHidden('hp:HP_Weapon', [hardpoint()], new Set()), false);
    });

    it('never hides the hull, whatever is destroyed', () => {
        assert.equal(partHidden('hull', [hardpoint()], new Set(['HP_Weapon'])), false);
    });
});

describe('effectPlays', () => {
    it('lights the damage smoke only once its hardpoint is gone', () => {
        const smoke = particle({ gate: 'HardpointDestroyed', hardpointId: 'HP_Weapon' });

        assert.equal(effectPlays(smoke, new Set()), false);
        assert.equal(effectPlays(smoke, new Set(['HP_Weapon'])), true);
    });

    it('puts the engine glow out when its hardpoint dies', () => {
        const glow = particle({
            systemRef: 'pe_engines', gate: 'HardpointAlive', hardpointId: 'HP_Engine',
        });

        assert.equal(effectPlays(glow, new Set()), true);
        assert.equal(effectPlays(glow, new Set(['HP_Engine'])), false);
    });

    it('leaves an ungated effect alone', () => {
        assert.equal(effectPlays(particle(), new Set(['HP_Weapon'])), true);
    });

    it('keeps an effect the model ships switched off switched off', () => {
        // pi_damage_elec_SD00 is present but invisible in the file; destroying something must not
        // suddenly reveal an effect the model never plays.
        const idle = particle({ startsVisible: false });

        assert.equal(effectPlays(idle, new Set(['HP_Weapon'])), false);
    });

    it('does not light smoke whose hardpoint is not the one destroyed', () => {
        const smoke = particle({ gate: 'HardpointDestroyed', hardpointId: 'HP_Left' });

        assert.equal(effectPlays(smoke, new Set(['HP_Right'])), false);
    });
});

describe('decalNames', () => {
    it('names the blast decal of every destroyed hardpoint', () => {
        // Damage_Decal names a mesh that shares its bone's name, and the file ships it VISIBLE - the
        // game hides it until the mount is blown off, so the preview has to do the same.
        const names = decalNames([hardpoint()], new Set(['HP_Weapon']));

        assert.deepEqual([...names], ['hp_f-l_blast']);
    });

    it('names nothing while everything is intact', () => {
        assert.equal(decalNames([hardpoint()], new Set()).size, 0);
    });

    it('skips a hardpoint that declares no decal', () => {
        const plain = hardpoint({ id: 'HP_Shield', damageDecalBone: null });

        assert.equal(decalNames([plain], new Set(['HP_Shield'])).size, 0);
    });

    it('lowercases, because mesh names are matched case-insensitively', () => {
        const shouty = hardpoint({ damageDecalBone: 'HP_F-L_BLAST' });

        assert.deepEqual([...decalNames([shouty], new Set(['HP_Weapon']))], ['hp_f-l_blast']);
    });
});

describe('destroyable', () => {
    it('lists only the hardpoints that can actually be destroyed', () => {
        const shield = hardpoint({ id: 'HP_Shield', isDestroyable: false });

        assert.deepEqual(destroyable([hardpoint(), shield]).map(h => h.id), ['HP_Weapon']);
    });
});

describe('playsOnOpen', () => {
    const gate = (over: Partial<Parameters<typeof playsOnOpen>[0]> = {}) => ({
        gate: PREVIEW_PARTICLE_GATE.always as string,
        systemRef: 'p_smoke_small',
        bone: 'Root',
        startsVisible: true,
        alt: null as number | null,
        lod: null as number | null,
        ...over,
    });

    /**
     * A model opens QUIET. Otherwise every damage fire, explosion and dust plume on a capital ship
     * fires the moment the file is opened - a wall of effects over the model the reader came to
     * look at, and the one-shot ones have already finished by the time they reach the dock.
     */
    it('keeps an ordinary effect off', () => {
        assert.equal(playsOnOpen(gate()), false);
    });

    it('plays an engine, which is what an intact model is actually doing', () => {
        assert.equal(playsOnOpen(gate({ gate: PREVIEW_PARTICLE_GATE.hardpointAlive })), true);
    });

    /**
     * A bare .alo has no XML, so nothing gates its engine glow and it arrives as `Always`. The name
     * is the only signal left, and it is a firm convention in the shipped data.
     */
    it('plays an engine glow named as one on a model with no hardpoints', () => {
        assert.equal(playsOnOpen(gate({ systemRef: 'p_engine_glow_small' })), true);
        assert.equal(playsOnOpen(gate({ bone: 'ENGINE_01' })), true);
    });

    it('still honours an effect the model itself ships switched off', () => {
        assert.equal(playsOnOpen(gate({
            gate: PREVIEW_PARTICLE_GATE.hardpointAlive, startsVisible: false,
        })), false);
    });

    /**
     * `Rv_mptl-2a.alo` carries its whole damage story on PROXIES - `p_smoke_small_thin_ALT2`,
     * `p_electricalstatic_ALT1` and nine more - and not one of its meshes has an ALT tag. Those
     * arrive ungated, because no hardpoint owns them, so the quiet-on-open rule held them off
     * permanently and the ALT control appeared to do nothing at all.
     *
     * It needs no suppressing: an ALT-tagged proxy is already hidden at ALT 0 by the level gate, so
     * the model still opens quiet, and selecting ALT 1 is a direct request to see exactly these.
     */
    it('leaves a level-tagged effect to the ALT and LOD gate', () => {
        assert.equal(playsOnOpen(gate({ systemRef: 'p_smoke_small_thin_ALT2', alt: 2 })), true);
        assert.equal(playsOnOpen(gate({ systemRef: 'p_fire_small', lod: 1 })), true);
    });

    it('keeps damage smoke off however it is named', () => {
        assert.equal(playsOnOpen(gate({
            gate: PREVIEW_PARTICLE_GATE.hardpointDestroyed, systemRef: 'p_engine_fire',
        })), false);
    });
});

describe('collisionMeshNames', () => {
    it('names the collision hull every hardpoint declares, lowercased for matching', () => {
        // `Collision_Mesh` is set on 267 of foc's hardpoints. Most of the geometry it names is
        // already hidden in the file - 186 of 187 - so this exists for the one that is not, and for
        // a mod that ships its hulls visible.
        const names = collisionMeshNames([
            hardpoint({ id: 'HP_FL', collisionMeshBone: 'HP_F-L_Coll' }),
            hardpoint({ id: 'HP_MC', collisionMeshBone: 'HP_M-C_Coll' }),
        ]);

        assert.deepEqual([...names].sort(), ['hp_f-l_coll', 'hp_m-c_coll']);
    });

    it('does not care whether the mount is intact', () => {
        // Unlike a decal. A collision hull is never drawn in either state - the engine consumes it
        // for hit testing and nothing else - so destroying the mount must not reveal it.
        const intact = collisionMeshNames([hardpoint({ collisionMeshBone: 'HP_F-L_Coll' })]);

        assert.equal(intact.has('hp_f-l_coll'), true);
    });

    it('leaves out a hardpoint that declares none', () => {
        assert.equal(collisionMeshNames([hardpoint({ collisionMeshBone: null })]).size, 0);
        assert.equal(collisionMeshNames([hardpoint({ collisionMeshBone: '' })]).size, 0);
    });

    it('leaves out a name that is really the mount ATTACH bone', () => {
        // MEASURED on the eaw Star Destroyer: the tractor beam and the fighter bay both give
        // `Collision_Mesh` the same value as `Attachment_Bone` - `HP_trac_bone` and `SPAWN_00`.
        // Hiding those would prune the whole subtree hanging off the attach point, which is the
        // mount itself, so a hardpoint that names its own attach bone is left alone.
        const names = collisionMeshNames([
            hardpoint({ id: 'HP_Trac', attachBone: 'HP_trac_bone', collisionMeshBone: 'HP_trac_bone' }),
            hardpoint({ id: 'HP_Bay', attachBone: 'SPAWN_00', collisionMeshBone: 'spawn_00' }),
        ]);

        assert.equal(names.size, 0);
    });
});

describe('hardpointGateAllows', () => {
    const gated = (over: Partial<PreviewParticle> = {}): PreviewParticle => ({
        id: 'p1', systemRef: 's', partId: 'hull', bone: 'b', boneIndex: 0,
        gate: PREVIEW_PARTICLE_GATE.always, startsVisible: false, ...over,
    });

    it('ignores startsVisible, which is a DEFAULT rather than a permission', () => {
        // An ability proxy ships switched off - that is what makes it an ability proxy - and
        // `prs_at-aa_fx` on the real AT-AA carries `startsVisible: false`. Reading that as a veto
        // meant no ability could ever light its own effect.
        assert.equal(hardpointGateAllows(gated({ startsVisible: false }), new Set()), true);
        assert.equal(hardpointGateAllows(gated({ startsVisible: true }), new Set()), true);
    });

    it('still refuses an effect whose mount is the wrong side of its gate', () => {
        // The damage question is a different one from the default question, and it still applies:
        // smoke that belongs to a destroyed mount must not appear because an ability is on.
        const onDeath = gated({
            gate: PREVIEW_PARTICLE_GATE.hardpointDestroyed, hardpointId: 'HP_Gun',
        });

        assert.equal(hardpointGateAllows(onDeath, new Set()), false);
        assert.equal(hardpointGateAllows(onDeath, new Set(['HP_Gun'])), true);
    });

    it('still refuses an alive-gated effect once its mount is gone', () => {
        const whileAlive = gated({
            gate: PREVIEW_PARTICLE_GATE.hardpointAlive, hardpointId: 'HP_Gun',
        });

        assert.equal(hardpointGateAllows(whileAlive, new Set()), true);
        assert.equal(hardpointGateAllows(whileAlive, new Set(['HP_Gun'])), false);
    });
});

