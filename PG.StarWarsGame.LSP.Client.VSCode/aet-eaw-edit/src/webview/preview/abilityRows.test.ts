// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import type { PreviewAbility, PreviewParticle } from '../../protocol/modelPreview';

import {
    abilityAllows, abilityClaims, abilityProxies, abilityRows, clipFor, revealsShield,
    shieldRevealed,
} from './abilityRows';

function ability(over: Partial<PreviewAbility> = {}): PreviewAbility {
    return { type: 'TURBO', proxyNames: [], modifiers: [], ...over };
}

function particle(id: string, bone: string): PreviewParticle {
    return {
        id, systemRef: `${bone}.alo`, partId: 'hull', bone, boneIndex: 0,
        gate: 'Always', startsVisible: true,
    };
}

describe('abilityProxies', () => {
    it('resolves a proxy BONE NAME to the particle ids on it', () => {
        const map = abilityProxies(
            [ability({ type: 'TURBO', proxyNames: ['PTE_Corvetteengines'] })],
            [particle('p1', 'PTE_Corvetteengines'), particle('p2', 'p_engine_glow')]);

        assert.deepEqual(map.get('TURBO'), ['p1']);
    });

    it('catches EVERY particle on a repeated proxy name', () => {
        // Proxy names repeat - a Star Destroyer has twenty of one name - so a bone name is not an
        // identity. Binding only the first would leave nineteen engines dark.
        const map = abilityProxies(
            [ability({ proxyNames: ['PTE_X'] })],
            [particle('p1', 'PTE_X'), particle('p2', 'PTE_X')]);

        assert.deepEqual(map.get('TURBO'), ['p1', 'p2']);
    });

    it('matches the bone name however either side cased it', () => {
        const map = abilityProxies(
            [ability({ proxyNames: ['pte_corvetteengines'] })],
            [particle('p1', 'PTE_Corvetteengines')]);

        assert.deepEqual(map.get('TURBO'), ['p1']);
    });

    it('is empty for an ability that names no proxy', () => {
        // Most abilities. 68 types exist and most drive nothing on the model.
        assert.equal(abilityProxies([ability()], [particle('p1', 'p_smoke')]).size, 0);
    });
});

describe('abilityAllows', () => {
    const proxies = abilityProxies(
        [ability({ type: 'TURBO', proxyNames: ['PTE_X'] })],
        [particle('p1', 'PTE_X'), particle('p2', 'p_smoke')]);

    it('holds an ability proxy off while its ability is inactive', () => {
        assert.equal(abilityAllows('p1', proxies, new Set()), false);
    });

    it('lets it play once the ability is activated', () => {
        assert.equal(abilityAllows('p1', proxies, new Set(['TURBO'])), true);
    });

    it('says nothing about a proxy no ability claims', () => {
        // The other 5404. This decider must not touch an ordinary effect, or activating one ability
        // would silence the whole model.
        assert.equal(abilityAllows('p2', proxies, new Set()), true);
        assert.equal(abilityAllows('p2', proxies, new Set(['TURBO'])), true);
    });

    it('holds a proxy claimed by two abilities until ONE of them is active', () => {
        const shared = abilityProxies([
            ability({ type: 'TURBO', proxyNames: ['PTE_X'] }),
            ability({ type: 'SPRINT', proxyNames: ['PTE_X'] }),
        ], [particle('p1', 'PTE_X')]);

        assert.equal(abilityAllows('p1', shared, new Set()), false);
        assert.equal(abilityAllows('p1', shared, new Set(['SPRINT'])), true);
    });
});

describe('clipFor', () => {
    const deploying = ability({
        deployClip: 'ev_at-aa_deploy_00.ala',
        undeployClip: 'ev_at-aa_undeploy_00.ala',
    });

    it('plays the deploy clip when the ability is switched on', () => {
        assert.equal(clipFor(deploying, true), 'ev_at-aa_deploy_00.ala');
    });

    it('plays the undeploy clip when it is switched off', () => {
        assert.equal(clipFor(deploying, false), 'ev_at-aa_undeploy_00.ala');
    });

    it('plays nothing when the model ships no clip for that direction', () => {
        // 25 models in foc ship a deploy and only 23 the matching undeploy - the X-Wing is one that
        // ships a deploy with NO undeploy - so switching it off must simply not call for a clip
        // rather than replaying the deploy backwards or throwing.
        const oneWay = ability({ deployClip: 'x_deploy_00.ala', undeployClip: null });

        assert.equal(clipFor(oneWay, true), 'x_deploy_00.ala');
        assert.equal(clipFor(oneWay, false), null);
        assert.equal(clipFor(ability(), true), null);
    });
});

describe('abilityRows', () => {
    it('names a row by its GUI name when it has one, and its type otherwise', () => {
        const rows = abilityRows([
            ability({ type: 'TURBO', guiName: 'Corvette_Turbo_Ability' }),
            ability({ type: 'SPREAD_OUT' }),
        ], new Map());

        assert.deepEqual(rows.map(r => r.label), ['Corvette_Turbo_Ability', 'SPREAD_OUT']);
        // The TYPE is always kept: it is what binds a proxy and what a modder searches for.
        assert.deepEqual(rows.map(r => r.type), ['TURBO', 'SPREAD_OUT']);
    });

    it('marks a row that drives nothing on the model', () => {
        // SPREAD_OUT and HUNT are orders. Most of the 68 types are, so the row says so rather than
        // offering a switch that would visibly do nothing.
        const rows = abilityRows([ability({ type: 'SPREAD_OUT' })], new Map());

        assert.equal(rows[0].drivesSomething, false);
        assert.match(rows[0].title, /nothing on this model/i);
    });

    it('counts a row as driving something when it has proxies', () => {
        const rows = abilityRows(
            [ability({ proxyNames: ['PTE_X'] })], new Map([['TURBO', ['p1', 'p2']]]));

        assert.equal(rows[0].drivesSomething, true);
        assert.equal(rows[0].proxyCount, 2);
        assert.match(rows[0].detail, /2 effects/);
    });

    it('counts a clip as driving something even with no proxies at all', () => {
        // SPOILER_LOCK is the X-Wing: a clip and nothing else.
        const rows = abilityRows([ability({ deployClip: 'x_deploy_00.ala' })], new Map());

        assert.equal(rows[0].drivesSomething, true);
        assert.match(rows[0].detail, /deploy/);
    });

    it('counts a bone or a declared particle as driving something', () => {
        assert.equal(abilityRows(
            [ability({ ownerAttachmentBone: 'B_Trooper_00' })], new Map())[0].drivesSomething, true);
        assert.equal(abilityRows(
            [ability({ particleEffect: 'Home_One_Target' })], new Map())[0].drivesSomething, true);
    });

    it('reads out the cadence it declares', () => {
        const rows = abilityRows(
            [ability({ rechargeSeconds: 60, expirationSeconds: 5 })], new Map());

        assert.match(rows[0].detail, /60s recharge/);
        assert.match(rows[0].detail, /5s/);
    });

    it('says so plainly when an ability declares no numbers at all', () => {
        assert.match(abilityRows([ability()], new Map())[0].detail, /no effects/i);
    });

    it('spells out what a STAT-ONLY ability does, since that is all it does', () => {
        // DEFEND on the Nebulon B: no proxy, no bone, no particle, no clip - only these. Reported
        // as a greyed switch it looked broken; the modifiers ARE the ability.
        const rows = abilityRows([ability({
            type: 'DEFEND',
            modifiers: [
                { stat: 'WEAPON_DELAY_MULTIPLIER', factor: 3 },
                { stat: 'SPEED_MULTIPLIER', factor: 0.8 },
            ],
        })], new Map());

        assert.match(rows[0].detail, /weapon delay x3/);
        assert.match(rows[0].detail, /speed x0\.8/);
    });

    it('still offers no SWITCH for a stat-only ability', () => {
        // There is nothing to show, so there is nothing to toggle. A disabled control there implied
        // the ability was broken rather than invisible.
        const rows = abilityRows([ability({
            modifiers: [{ stat: 'SPEED_MULTIPLIER', factor: 0.8 }],
        })], new Map());

        assert.equal(rows[0].drivesSomething, false);
    });
});

describe('abilityClaims', () => {
    const proxies = abilityProxies(
        [ability({ type: 'TURBO', proxyNames: ['PTE_X'] })],
        [particle('p1', 'PTE_X'), particle('p2', 'p_smoke')]);

    it('says which proxies an ability owns', () => {
        assert.equal(abilityClaims('p1', proxies), true);
        assert.equal(abilityClaims('p2', proxies), false);
    });

    it('is what lets the ability OVERRIDE the quiet-on-open rule', () => {
        // The opening rules hold every non-engine effect back on open, and that is right - but they
        // are about the first MOMENT, not a permanent veto. Ability proxies are claimed, so the
        // ability decides them outright; without this, switching an ability on changed nothing at
        // all and the row looked broken. Found on the live AT-AA.
        assert.equal(abilityClaims('p1', proxies), true);
    });
});

describe('abilities that reveal the shield mesh', () => {
    it('knows DEFEND shows it', () => {
        // Told 2026-08-23. The shield mesh ships `alamoHidden: true` - MeshShield.fx on
        // `Ev_stardestroyer.alo` - so it is invisible until something reveals it, and DEFEND is
        // what does. This was the channel that made DEFEND look like a dead switch.
        assert.equal(revealsShield('DEFEND'), true);
    });

    it('does not claim it for an ability that merely has a shield-ish name', () => {
        // A table, not a name test: SHIELD_FLARE and MISSILE_SHIELD are different abilities and
        // guessing from the word would light the mesh for both.
        assert.equal(revealsShield('MISSILE_SHIELD'), false);
        assert.equal(revealsShield('TURBO'), false);
    });

    it('matches however the XML cased the type', () => {
        assert.equal(revealsShield('defend'), true);
    });

    it('makes such an ability worth a switch even with no proxy or clip', () => {
        // The whole point: DEFEND declares no proxy, no bone, no particle and no clip. Without this
        // it read `drivesSomething: false` and got a dead control.
        const rows = abilityRows([ability({ type: 'DEFEND' })], new Map());

        assert.equal(rows[0].drivesSomething, true);
        assert.match(rows[0].detail, /shield/i);
    });

    it('says nothing about the shield for an ability that does not reveal it', () => {
        assert.doesNotMatch(abilityRows([ability({ type: 'TURBO' })], new Map())[0].detail, /shield/i);
    });
});

describe('shieldRevealed', () => {
    it('is on while any revealing ability is active', () => {
        assert.equal(shieldRevealed(new Set(['DEFEND'])), true);
        assert.equal(shieldRevealed(new Set(['TURBO', 'DEFEND'])), true);
    });

    it('is off with none of them active', () => {
        assert.equal(shieldRevealed(new Set()), false);
        assert.equal(shieldRevealed(new Set(['TURBO'])), false);
    });
});
