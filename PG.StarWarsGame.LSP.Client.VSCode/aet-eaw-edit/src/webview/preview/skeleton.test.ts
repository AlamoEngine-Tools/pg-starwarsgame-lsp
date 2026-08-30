// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
    alamoBoneName, ancestorsOf, buildBoneTree, filterBones, labelCandidates, visibleRows,
    type BoneAttachment, type FlatBone,
} from './skeleton';

function bone(index: number, name: string, parent: number, visible = true): FlatBone {
    return { index, name, parent, visible };
}

/** A miniature of the Star Destroyer's shape: hull, hardpoint bones, damage proxies under them. */
const DESTROYER: FlatBone[] = [
    bone(0, 'ROOT', -1),
    bone(1, 'HP_F-L_Bone', 0),
    bone(2, 'HP_F-L_EmitDamage', 0),
    bone(3, 'p_hp_imperial_damage', 2),
    bone(4, 'HP_F-R_Bone', 0),
    bone(5, 'Engine_Glow', 0, false),
];

describe('alamoBoneName', () => {
    /** The suffix exists because a hull carries twenty bones with the same name. */
    it('recovers the name the XML references from the unique node name', () => {
        assert.deepEqual(alamoBoneName('HP_F-L_Bone#1'), { name: 'HP_F-L_Bone', index: 1 });
    });

    it('keeps a name that itself contains a hash', () => {
        assert.deepEqual(alamoBoneName('odd#name#7'), { name: 'odd#name', index: 7 });
    });

    it('rejects a node name that carries no index', () => {
        assert.equal(alamoBoneName('HP_F-L_Bone'), null);
        assert.equal(alamoBoneName('#3'), null);
        assert.equal(alamoBoneName('bone#notanumber'), null);
    });
});

describe('buildBoneTree', () => {
    it('nests children under their parent and records depth', () => {
        const roots = buildBoneTree(DESTROYER);

        assert.equal(roots.length, 1);

        const root = roots[0];
        assert.equal(root.name, 'ROOT');
        assert.equal(root.depth, 0);

        const emitDamage = root.children.find(c => c.name === 'HP_F-L_EmitDamage')!;
        assert.equal(emitDamage.depth, 1);
        assert.equal(emitDamage.children[0].name, 'p_hp_imperial_damage');
        assert.equal(emitDamage.children[0].depth, 2);
    });

    it('carries the visibility flag through', () => {
        const roots = buildBoneTree(DESTROYER);

        assert.equal(roots[0].children.find(c => c.name === 'Engine_Glow')!.visible, false);
    });

    it('attaches what is attached to a bone', () => {
        const attachments = new Map<number, BoneAttachment[]>([
            [1, [{ kind: 'hardpoint', label: 'HP_SD_Weapon_FL' }]],
        ]);

        const roots = buildBoneTree(DESTROYER, attachments);
        const hardpointBone = roots[0].children.find(c => c.name === 'HP_F-L_Bone')!;

        assert.deepEqual(hardpointBone.attachments, [{ kind: 'hardpoint', label: 'HP_SD_Weapon_FL' }]);
    });

    /**
     * A bone whose parent index is nonsense still exists and still anchors whatever is attached to it.
     * Dropping it would hide geometry that is plainly on screen, so it becomes a root instead.
     */
    it('treats an impossible parent as a root rather than losing the bone', () => {
        const roots = buildBoneTree([
            bone(0, 'ROOT', -1),
            bone(1, 'ORPHAN', 99),
            bone(2, 'SELF', 2),
        ]);

        assert.deepEqual(roots.map(r => r.name).sort(), ['ORPHAN', 'ROOT', 'SELF']);
    });

    it('handles an empty skeleton', () => {
        assert.deepEqual(buildBoneTree([]), []);
    });
});

describe('filterBones', () => {
    const roots = buildBoneTree(DESTROYER);

    it('returns everything for an empty query', () => {
        assert.equal(filterBones(roots, '   ').length, roots.length);
    });

    /** Filtering to HP_ is the common case; the hardpoint bones follow a strict naming convention. */
    it('keeps matches and the ancestors that make them reachable', () => {
        const filtered = filterBones(roots, 'HP_F-L');

        // ROOT is not a match but has to survive, or the matches have nothing to hang from.
        const root = filtered[0];
        assert.equal(root.name, 'ROOT');
        assert.deepEqual(
            root.children.map(c => c.name).sort(), ['HP_F-L_Bone', 'HP_F-L_EmitDamage']);
    });

    /**
     * Typing HP_ should give the hardpoint bones, not also the twenty damage proxies attached to
     * them - the subtree is what the user is filtering away.
     */
    it('drops the children of a match', () => {
        const filtered = filterBones(roots, 'EmitDamage');
        const match = filtered[0].children[0];

        assert.equal(match.name, 'HP_F-L_EmitDamage');
        assert.deepEqual(match.children, []);
    });

    it('matches case-insensitively', () => {
        assert.equal(filterBones(roots, 'hp_f-r').length, 1);
    });

    it('returns nothing when no bone matches', () => {
        assert.deepEqual(filterBones(roots, 'nothing_is_called_this'), []);
    });

    it('does not mutate the tree it filtered', () => {
        filterBones(roots, 'EmitDamage');

        const emitDamage = roots[0].children.find(c => c.name === 'HP_F-L_EmitDamage')!;
        assert.equal(emitDamage.children.length, 1, 'the original subtree was emptied');
    });
});

describe('visibleRows', () => {
    const roots = buildBoneTree(DESTROYER);

    /** A skeleton is shallow; hiding it behind twisties on open makes the tree useless at a glance. */
    it('shows the whole tree when nothing is collapsed', () => {
        assert.equal(visibleRows(roots, new Set()).length, DESTROYER.length);
    });

    it('hides the subtree of a collapsed node but keeps the node', () => {
        const emitDamage = roots[0].children.find(c => c.name === 'HP_F-L_EmitDamage')!;

        const rows = visibleRows(roots, new Set([emitDamage.index]));

        assert.ok(rows.some(r => r.node.name === 'HP_F-L_EmitDamage'));
        assert.ok(!rows.some(r => r.node.name === 'p_hp_imperial_damage'));
    });

    it('marks which rows can be expanded', () => {
        const rows = visibleRows(roots, new Set());

        assert.equal(rows.find(r => r.node.name === 'HP_F-L_EmitDamage')!.expandable, true);
        assert.equal(rows.find(r => r.node.name === 'HP_F-R_Bone')!.expandable, false);
    });

    it('collapsing a root hides everything under it', () => {
        assert.equal(visibleRows(roots, new Set([0])).length, 1);
    });
});

describe('labelCandidates', () => {
    it('labels nothing when off', () => {
        assert.equal(labelCandidates(DESTROYER, 'none', new Set([3])).size, 0);
    });

    it('labels only the selection', () => {
        assert.deepEqual([...labelCandidates(DESTROYER, 'selected', new Set([3]))], [3]);
    });

    it('labels nothing in selected mode with no selection', () => {
        assert.equal(labelCandidates(DESTROYER, 'selected', new Set()).size, 0);
    });

    it('labels every bone when asked for all', () => {
        assert.equal(labelCandidates(DESTROYER, 'all', new Set()).size, DESTROYER.length);
    });
});

describe('ancestorsOf', () => {
    /** Selecting a bone in the viewport has to reveal it in the tree, which means expanding its path. */
    it('gives the path from the root down to the bone', () => {
        assert.deepEqual(ancestorsOf(DESTROYER, 3), [0, 2]);
    });

    it('is empty for a root', () => {
        assert.deepEqual(ancestorsOf(DESTROYER, 0), []);
    });

    /** A malformed file could describe a parent cycle; the panel must not hang on it. */
    it('terminates on a cycle', () => {
        const cyclic = [bone(0, 'A', 1), bone(1, 'B', 0)];

        assert.ok(ancestorsOf(cyclic, 0).length <= cyclic.length + 1);
    });
});

describe('labelCandidates, over a selection of several', () => {
    const bones = [
        { index: 0, name: 'Root' }, { index: 1, name: 'A' }, { index: 2, name: 'B' },
    ] as never;

    it('names every selected bone, not only one of them', () => {
        // The panel selects several at once - two fire bones, a shift-run in the tree - and one
        // label for a selection of three says the other two are not selected.
        assert.deepEqual(
            [...labelCandidates(bones, 'selected', new Set([1, 2]))].sort(), [1, 2]);
    });

    it('names none when nothing is selected', () => {
        assert.equal(labelCandidates(bones, 'selected', new Set()).size, 0);
    });

    it('still names everything in `all`, whatever is selected', () => {
        assert.equal(labelCandidates(bones, 'all', new Set([1])).size, 3);
    });

    it('names nothing in `none`', () => {
        assert.equal(labelCandidates(bones, 'none', new Set([1, 2])).size, 0);
    });
});
