// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import type { PreviewAbility, PreviewParticle } from '../../protocol/modelPreview';

import {
    abilityAllows, abilityBarTitle, abilityClaims, abilityFacts, abilityOwnership, abilityProxies,
    abilityRows, clipFor, gotoDefinitionTitle, revealsShield, revealsStealth, shieldRevealed,
    stealthed,
    unboundEffectIds,
} from './abilityRows';

function ability(over: Partial<PreviewAbility> = {}): PreviewAbility {
    return { type: 'TURBO', proxyNames: [], modifiers: [], ...over };
}

function particle(id: string, bone: string, claimsAbility?: string): PreviewParticle {
    return {
        id, systemRef: `${bone}.alo`, partId: 'hull', bone, boneIndex: 0,
        gate: 'Always', startsVisible: true, claimsAbility,
    };
}

describe('revealsStealth', () => {
    it('takes both types the shipped units declare', () => {
        // A table and not a name test, because the two do not share a word. `TIE_Phantom`,
        // `Vengeance_Frigate`, `Tyber_Zann` and `Urai_Fen` declare STEALTH; `Luke_Skywalker_Jedi`
        // declares FORCE_CLOAK and carries the same `stealth` mesh. Guessing from the name would
        // have left Luke permanently wearing his cloak shell.
        assert.equal(revealsStealth('STEALTH'), true);
        assert.equal(revealsStealth('FORCE_CLOAK'), true);
    });

    it('leaves the other abilities alone', () => {
        for (const type of ['DEFEND', 'MISSILE_SHIELD', 'TURBO', 'SPREAD_OUT']) {
            assert.equal(revealsStealth(type), false, type);
        }
    });

    it('reads a type however it is cased', () => {
        assert.equal(revealsStealth(' stealth '), true);
    });
});

describe('stealthed', () => {
    it('is on while any active ability cloaks the unit', () => {
        assert.equal(stealthed(new Set(['STEALTH'])), true);
        assert.equal(stealthed(new Set(['SELF_DESTRUCT', 'STEALTH'])), true);
        assert.equal(stealthed(new Set(['FORCE_CLOAK'])), true);
    });

    it('is off when nothing is cloaking it', () => {
        // Which is the state a unit that cannot cloak at all is permanently in - and that is what
        // keeps the shell off `Tyber_Zann_Prisoner`, who uses Tyber's model and declares nothing.
        assert.equal(stealthed(new Set()), false);
        assert.equal(stealthed(new Set(['DEFEND', 'TURBO'])), false);
    });
});

describe('unboundEffectIds', () => {
    // The Tartan, which is the shipped case: `Tartan_Patrol_Cruiser` declares POWER_TO_WEAPONS and
    // its model carries `pptw_ptwsa` for it plus two `Pte_tartanengine_*` for a TURBO it does not
    // have. The engine never shows those two; we did.
    const TARTAN = [
        particle('pptw', 'pptw_ptwsa', 'POWER_TO_WEAPONS'),
        particle('lrg', 'Pte_tartanengine_lrg', 'TURBO'),
        particle('sml', 'Pte_tartanengine_sml', 'TURBO'),
        particle('glow', 'p_engine_glow'),
    ];

    it('names the effects whose ability the unit never declares', () => {
        const unbound = unboundEffectIds([ability({ type: 'POWER_TO_WEAPONS' })], TARTAN);

        assert.deepEqual([...unbound].sort(), ['lrg', 'sml']);
    });

    it('leaves an effect alone once the unit declares its ability', () => {
        const unbound = unboundEffectIds(
            [ability({ type: 'POWER_TO_WEAPONS' }), ability({ type: 'TURBO' })], TARTAN);

        assert.equal(unbound.size, 0);
    });

    it('binds nothing when the unit declares no abilities at all', () => {
        // The case an unbound effect is most likely to be a real mistake in, and the one a
        // `declared.length > 0` guard would quietly skip.
        const unbound = unboundEffectIds([], TARTAN);

        assert.deepEqual([...unbound].sort(), ['lrg', 'pptw', 'sml']);
    });

    it('never touches an ordinary effect', () => {
        // 5404 proxies claim nothing against 39 that do. A claim of null is not an unbound claim.
        for (const declared of [[], [ability({ type: 'TURBO' })]]) {
            assert.equal(unboundEffectIds(declared, TARTAN).has('glow'), false);
        }
    });

    it('matches the ability type without regard to case', () => {
        // `Ev_acclamator` writes `power_to_weapons` in lower case, and the proxy names are written
        // both ways too - `pptw_2mtank` beside `PTE_Corvetteengines`.
        const unbound = unboundEffectIds([ability({ type: 'power_to_weapons' })], TARTAN);

        assert.equal(unbound.has('pptw'), false);
    });
});

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

describe('abilityOwnership', () => {
    const proxies = abilityProxies([
        ability({ type: 'TURBO', proxyNames: ['PTE_X'] }),
        ability({ type: 'POWER_TO_WEAPONS', proxyNames: ['PPTW_Y'] }),
    ], [particle('p1', 'PTE_X'), particle('p2', 'PPTW_Y'), particle('p3', 'p_smoke')]);

    it('names the systems the ability itself drives', () => {
        assert.deepEqual(abilityOwnership('TURBO', proxies).systemIds, ['p1']);
    });

    it('leaves another ability`s systems alone', () => {
        // The claim is what switching this ability takes back from the reader, so it has to be
        // exactly this ability's rows. Taking back everything would undo a mesh the reader hid for
        // an unrelated reason - which is the global `clearRowOverrides` a clip does, and a clip
        // has the excuse of resetting the whole pose.
        const claimed = abilityOwnership('TURBO', proxies).systemIds;

        assert.equal(claimed.includes('p2'), false);
        assert.equal(claimed.includes('p3'), false);
    });

    it('claims the shield mesh for DEFEND, which drives no proxy at all', () => {
        // DEFEND declares no proxy, no bone, no particle and no clip: revealing the shield mesh is
        // the whole of what it shows. Its claim is the mesh or it is nothing.
        const claim = abilityOwnership('DEFEND', proxies);

        assert.equal(claim.shieldMesh, true);
        assert.deepEqual(claim.systemIds, []);
    });

    it('claims the stealth shell for both types that cloak', () => {
        assert.equal(abilityOwnership('STEALTH', proxies).stealthShell, true);
        assert.equal(abilityOwnership('FORCE_CLOAK', proxies).stealthShell, true);
    });

    it('claims nothing for an ability that drives nothing on this model', () => {
        const claim = abilityOwnership('SPREAD_OUT', proxies);

        assert.deepEqual(claim.systemIds, []);
        assert.equal(claim.shieldMesh, false);
        assert.equal(claim.stealthShell, false);
    });
});

describe('clipFor', () => {
    const deploying = ability({
        deployClip: 'ev_at-aa_deploy_00.ala',
        undeployClip: 'ev_at-aa_undeploy_00.ala',
    });

    // The XML names a FILE and the mixer holds STEMS. Measured on the X-Wing: the ability declares
    // `rv_xwing_deploy_00.ala` and the loaded glTF clip is called `rv_xwing_deploy_00`, so the
    // viewport's `clips.find(c => c.name === name)` missed every single time - and missed SILENTLY,
    // which is why the panel went on reporting a clip that was never running.
    it('plays the deploy clip when the ability is switched on', () => {
        assert.equal(clipFor(deploying, true), 'ev_at-aa_deploy_00');
    });

    it('plays the undeploy clip when it is switched off', () => {
        assert.equal(clipFor(deploying, false), 'ev_at-aa_undeploy_00');
    });

    it('takes the extension off however it was written', () => {
        assert.equal(clipFor(ability({ deployClip: 'A_Deploy_00.ALA' }), true), 'A_Deploy_00');
    });

    it('leaves a name that is already a stem alone', () => {
        // Nothing guarantees a mod writes the extension, and stripping a suffix that is not there
        // must not eat part of the name.
        assert.equal(clipFor(ability({ deployClip: 'rv_xwing_deploy_00' }), true),
            'rv_xwing_deploy_00');
    });

    it('only takes off an ALA extension, not any trailing dot', () => {
        assert.equal(clipFor(ability({ deployClip: 'weird.name_00' }), true), 'weird.name_00');
    });

    it('plays nothing when the model ships no clip for that direction', () => {
        // 25 models in foc ship a deploy and only 23 the matching undeploy - the X-Wing is one that
        // ships a deploy with NO undeploy - so switching it off must simply not call for a clip
        // rather than replaying the deploy backwards or throwing.
        const oneWay = ability({ deployClip: 'x_deploy_00.ala', undeployClip: null });

        assert.equal(clipFor(oneWay, true), 'x_deploy_00');
        assert.equal(clipFor(oneWay, false), null);
        assert.equal(clipFor(ability(), true), null);
        assert.equal(clipFor(ability({ deployClip: '   ' }), true), null);
    });
});

describe('abilityRows', () => {
    it('names a row the way the command bar does, and its type otherwise', () => {
        const rows = abilityRows([
            ability({ type: 'TURBO', name: 'Turbo Boost' }),
            ability({ type: 'SPREAD_OUT' }),
        ], new Map());

        assert.deepEqual(rows.map(r => r.label), ['Turbo Boost', 'SPREAD_OUT']);
        // The TYPE is always kept: it is what binds a proxy and what a modder searches for.
        assert.deepEqual(rows.map(r => r.type), ['TURBO', 'SPREAD_OUT']);
    });

    it('never labels a row with the symbol GUI_Activated_Ability_Name points at', () => {
        // That tag names a SpecialAbility BLOCK, not display text. Using it as the label put an
        // identifier where the reader expects the words the game shows - and it is present on 39%
        // of abilities, so it was the common case.
        const rows = abilityRows(
            [ability({ type: 'TURBO', guiName: 'Corvette_Turbo_Ability' })], new Map());

        assert.equal(rows[0].label, 'TURBO');
    });

    it('carries the localised description and the command-bar icon through', () => {
        const rows = abilityRows([ability({
            name: 'Turbo Boost',
            description: 'Briefly increases speed.',
            iconDataUri: 'data:image/png;base64,AAA',
        })], new Map());

        assert.equal(rows[0].description, 'Briefly increases speed.');
        assert.equal(rows[0].iconDataUri, 'data:image/png;base64,AAA');
    });

    it('offers a jump only for an ability that names a definition', () => {
        // The name is a reference, and resolving it is the server's job - the row only carries it
        // so the panel knows whether there is anything to offer.
        const [named, unnamed] = abilityRows([
            ability({ type: 'TURBO', guiName: 'Corvette_Turbo_Ability' }),
            ability({ type: 'SPREAD_OUT' }),
        ], new Map());

        assert.equal(named.definition, 'Corvette_Turbo_Ability');
        assert.equal(unnamed.definition, null);
    });

    it('ignores a blank GUI name rather than offering a jump to nothing', () => {
        // The shipped data writes the tag empty in places - Groundvehicles.xml has one with only
        // whitespace between the tags.
        assert.equal(abilityRows([ability({ guiName: '  ' })], new Map())[0].definition, null);
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

describe('gotoDefinitionTitle', () => {
    it('says what will open', () => {
        const [row] = abilityRows(
            [ability({ type: 'TURBO', guiName: 'Corvette_Turbo_Ability' })], new Map());

        assert.match(gotoDefinitionTitle(row), /Corvette_Turbo_Ability/);
    });

    it('says WHY it cannot, rather than leaving a dead control unexplained', () => {
        // The button stays on the row when there is nothing to open - a control that came and went
        // per row would teach nobody it exists - so the title is the whole of the explanation.
        const [row] = abilityRows([ability({ type: 'SPREAD_OUT' })], new Map());

        assert.match(gotoDefinitionTitle(row), /SPREAD_OUT/);
        assert.match(gotoDefinitionTitle(row), /nothing to open/i);
    });
});

describe('abilityBarTitle', () => {
    it('leads with the words the game uses, keeping the type in reach', () => {
        // The bar draws an ICON, so the tooltip is the only place the name appears at all. The
        // type stays because it is what binds a proxy and what a modder searches for.
        const [row] = abilityRows(
            [ability({ type: 'LUCKY_SHOT', name: 'Lucky Shot' })], new Map());

        assert.match(abilityBarTitle(row), /^Lucky Shot \(LUCKY_SHOT\)/);
    });

    it('does not repeat the type when that is all the row has', () => {
        const [row] = abilityRows([ability({ type: 'SPREAD_OUT' })], new Map());

        assert.equal(abilityBarTitle(row).split('\n')[0], 'SPREAD_OUT');
    });

    it('carries the description and what the ability drives', () => {
        const [row] = abilityRows([ability({
            type: 'LUCKY_SHOT', name: 'Lucky Shot',
            description: 'A shot with a huge damage bonus.',
            proxyNames: ['PTE_X'],
        })], new Map([['LUCKY_SHOT', ['p1', 'p2']]]));

        const title = abilityBarTitle(row);
        assert.match(title, /huge damage bonus/);
        assert.match(title, /2 effects/);
    });

    it('says an order drives nothing, since its key is disabled and must explain itself', () => {
        const [row] = abilityRows([ability({ type: 'SPREAD_OUT' })], new Map());

        assert.match(abilityBarTitle(row), /nothing on this model/i);
    });
});

const row = (over: Partial<PreviewAbility>) =>
    abilityRows([ability(over)], new Map())[0];

describe('abilityFacts', () => {
    /**
     * The card's split: what the GAME tells a player, and what the FILE says.
     *
     * They were one joined sentence under the name - "2 effects - on HP_Bone - speed x0.8 - 60s
     * recharge" - sitting directly beneath the localised description, so the reader's own prose and
     * the modder's measurements ran together into one paragraph with no seam.
     */
    it('is a LIST of labelled values, like the hardpoint card`s', () => {
        const facts = abilityFacts(row({ rechargeSeconds: 60, expirationSeconds: 15 }));

        assert.ok(facts.every(pair => pair.length === 2), 'label and value');
        assert.ok(facts.some(([label]) => /recharge/i.test(label)));
        assert.ok(facts.some(([label]) => /last|expir/i.test(label)));
    });

    it('names the SpecialAbility block, which is an identifier and not display text', () => {
        // guiName is on 39% of shipped abilities and is what a jump resolves. It belongs in the
        // facts, never as the card's title - that mistake is what put Corvette_Turbo_Ability where
        // the reader expected "Turbo Boost".
        const facts = abilityFacts(row({ guiName: 'Executor_Tractor_Beam_Attack_Ability' }));

        assert.ok(facts.some(([, value]) => value === 'Executor_Tractor_Beam_Attack_Ability'));
    });

    it('omits what the file does not declare', () => {
        assert.deepEqual(abilityFacts(row({})).filter(([, value]) => value === ''), []);
    });

    it('says nothing at all for an ability that declares nothing', () => {
        assert.deepEqual(abilityFacts(row({})), []);
    });
});
