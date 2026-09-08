// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
    boneIndexOfRow, boneRowId, buildTree, defaultCollapsed, effectPlacement, filterTree,
    meshPlacement, selectionAfterClick,
    toggleSelected, toggleTargets, visibleTreeRows,
    withDescendants, type TreeItem, type TreeKind,
} from './previewTree';

const ALL: ReadonlySet<TreeKind> = new Set<TreeKind>(['bone', 'mesh', 'particle']);

const item = (
    id: string, kind: TreeKind, name: string, parentId: string | null,
): TreeItem => ({ id, kind, name, parentId, visible: true, gatedOff: false });

// A bantha-shaped model: a root bone carrying meshes, a child bone chain, and a particle system.
const ITEMS: TreeItem[] = [
    item('b:0', 'bone', 'Root', null),
    item('b:1', 'bone', 'B_Body', 'b:0'),
    item('b:2', 'bone', 'B_Head', 'b:1'),
    item('m:a', 'mesh', 'Bantha#0', 'b:0'),
    item('m:b', 'mesh', 'Bantha_Skirt#0', 'b:0'),
    item('m:c', 'mesh', 'Head#0', 'b:2'),
    item('e:x', 'particle', 'p_smoke', 'b:2'),
];

describe('buildTree', () => {
    it('nests everything under the bone it is attached to', () => {
        const [root] = buildTree(ITEMS);

        assert.equal(root.name, 'Root');
        assert.deepEqual(root.children.map(c => c.name), ['B_Body', 'Bantha#0', 'Bantha_Skirt#0']);
    });

    it('puts child bones before a bone`s own meshes and particle systems', () => {
        // Otherwise a bone's geometry gets lost among a long list of child bones.
        const head = buildTree(ITEMS)[0].children[0].children[0];

        assert.deepEqual(head.children.map(c => c.kind), ['mesh', 'particle']);
    });

    it('records depth so rows can be indented', () => {
        const [root] = buildTree(ITEMS);

        assert.equal(root.depth, 0);
        assert.equal(root.children[0].depth, 1);
        assert.equal(root.children[0].children[0].depth, 2);
    });

    it('keeps an item whose parent is missing, as a root', () => {
        // It still anchors something the viewport is drawing; dropping it would hide that.
        const roots = buildTree([item('m:orphan', 'mesh', 'Stray#0', 'b:404')]);

        assert.deepEqual(roots.map(r => r.name), ['Stray#0']);
    });
});

describe('filterTree', () => {
    const roots = buildTree(ITEMS);

    /**
     * A kind that is not ticked NEVER shows, however it got there.
     *
     * Reported: *"Some of them have the icon for a particle, but don't get filtered even though they
     * are children/leaves and you filter e.g. particle system out."* A node that MATCHED kept all of
     * its children unfiltered - so a bone that survived a Bones-only filter dragged its particles in
     * with it, icon and all.
     *
     * The reasoning for that was "a match's own children are the CONTENTS of what was found" - which
     * is right for a text SEARCH and wrong for a kind FILTER. The two are separated now.
     */
    it('never shows a kind that is not ticked, even under a match', () => {
        const filtered = filterTree(roots, { text: '', kinds: new Set<TreeKind>(['bone']) });
        const rows = visibleTreeRows(filtered, new Set());

        assert.deepEqual(rows.map(r => r.node.kind).filter(k => k !== 'bone'), []);
        assert.deepEqual(rows.map(r => r.node.name), ['Root', 'B_Body', 'B_Head']);
    });

    it('drops a particle leaf when meshes and bones are asked for', () => {
        const filtered = filterTree(roots, {
            text: '', kinds: new Set<TreeKind>(['bone', 'mesh']),
        });
        const rows = visibleTreeRows(filtered, new Set());

        assert.equal(rows.some(r => r.node.name === 'p_smoke'), false);
    });

    /**
     * A TEXT hit still shows what is inside it - that is what searching for a bone by name is for -
     * but only of the kinds that are ticked. Text is a search; kind is a filter.
     */
    it('shows the contents of a text hit, still obeying the kinds', () => {
        const all = filterTree(roots, {
            text: 'B_Head', kinds: new Set<TreeKind>(['bone', 'mesh', 'particle']),
        });

        assert.deepEqual(
            visibleTreeRows(all, new Set()).map(r => r.node.name),
            ['Root', 'B_Body', 'B_Head', 'Head#0', 'p_smoke']);

        const noEffects = filterTree(roots, {
            text: 'B_Head', kinds: new Set<TreeKind>(['bone', 'mesh']),
        });

        assert.deepEqual(
            visibleTreeRows(noEffects, new Set()).map(r => r.node.name),
            ['Root', 'B_Body', 'B_Head', 'Head#0']);
    });

    it('keeps only the kinds asked for, plus the ancestors that lead to them', () => {
        const filtered = filterTree(roots, { text: '', kinds: new Set<TreeKind>(['particle']) });
        const rows = visibleTreeRows(filtered, new Set());

        assert.deepEqual(rows.map(r => `${r.node.kind}:${r.node.name}`), [
            'bone:Root', 'bone:B_Body', 'bone:B_Head', 'particle:p_smoke',
        ]);
    });

    it('does not let a retained ancestor drag its other children back in', () => {
        // B_Head is kept only because it carries the particle system, so its mesh stays filtered out.
        const filtered = filterTree(roots, { text: '', kinds: new Set<TreeKind>(['particle']) });
        const names = visibleTreeRows(filtered, new Set()).map(r => r.node.name);

        assert.equal(names.includes('Head#0'), false);
    });

    it('matches on text as well as kind', () => {
        const filtered = filterTree(roots, { text: 'skirt', kinds: ALL });
        const names = visibleTreeRows(filtered, new Set()).map(r => r.node.name);

        assert.equal(names.includes('Bantha_Skirt#0'), true);
        assert.equal(names.includes('Bantha#0'), false);
    });

    it('shows nothing when every kind is unticked', () => {
        assert.deepEqual(filterTree(roots, { text: '', kinds: new Set() }), []);
    });

    it('shows everything when nothing is asked for', () => {
        const rows = visibleTreeRows(filterTree(roots, { text: '', kinds: ALL }), new Set());

        assert.equal(rows.length, ITEMS.length);
    });

    it('marks the rows kept only to reach a match as SCAFFOLDING', () => {
        // The reported fault: filtering to effects keeps the bones on the path so the match stays
        // reachable, and those bones then rendered exactly like the thing being looked for. On a
        // model whose effects are switched off - greyed and italic - the scaffolding was the
        // LOUDER half of the list.
        const filtered = filterTree(roots, { text: '', kinds: new Set<TreeKind>(['particle']) });
        const rows = visibleTreeRows(filtered, new Set());

        assert.deepEqual(rows.map(r => `${r.node.name}:${r.node.scaffold === true}`), [
            'Root:true', 'B_Body:true', 'B_Head:true', 'p_smoke:false',
        ]);
    });

    it('marks nothing when the filter asks for everything', () => {
        // No filter, no scaffolding: every row is there on its own account.
        const rows = visibleTreeRows(filterTree(roots, { text: '', kinds: ALL }), new Set());

        assert.equal(rows.some(r => r.node.scaffold === true), false);
    });

    it('does not call a row scaffolding just because its parent matched', () => {
        // A match keeps its own children whole. Those are the CONTENTS of what was found, not a
        // path to it, and dimming them would hide the very thing the filter turned up.
        const filtered = filterTree(roots, { text: 'B_Head', kinds: ALL });
        const rows = visibleTreeRows(filtered, new Set());
        const head = rows.find(r => r.node.name === 'B_Head');

        assert.equal(head?.node.scaffold, false);
        assert.equal(rows.filter(r => r.node.depth > head!.node.depth)
            .some(r => r.node.scaffold === true), false);
    });
});

describe('visibleTreeRows', () => {
    it('never folds a row that is only there to reach a match', () => {
        // A scaffold row exists for ONE reason: the match below it. Honouring a fold on it hides
        // that match and leaves a row whose whole purpose is now invisible - which is what a
        // filtered Star Destroyer did, showing 2 of its 22 effect rows and eighteen bones.
        const filtered = filterTree(
            buildTree(ITEMS), { text: '', kinds: new Set<TreeKind>(['particle']) });
        const everything = new Set(ITEMS.map(item => item.id));
        const names = visibleTreeRows(filtered, everything).map(r => r.node.name);

        assert.equal(names.includes('p_smoke'), true);
    });

    it('still folds a match, because the reader folded that one on purpose', () => {
        // The rule is about scaffolding, not about filters. A row the filter actually found keeps
        // its twisty - its children are its contents, and folding them away is a fair thing to ask.
        const filtered = filterTree(buildTree(ITEMS), { text: 'B_Head', kinds: ALL });
        const rows = visibleTreeRows(filtered, new Set(['b:2']));

        assert.equal(rows.some(r => r.node.name === 'B_Head'), true);
        assert.equal(rows.some(r => r.node.name === 'p_smoke'), false);
    });

    it('stops at a collapsed node', () => {
        const rows = visibleTreeRows(buildTree(ITEMS), new Set(['b:1']));
        const names = rows.map(r => r.node.name);

        assert.equal(names.includes('B_Body'), true);
        assert.equal(names.includes('B_Head'), false);
    });

    it('reports whether a row has anything to reveal', () => {
        const rows = visibleTreeRows(buildTree(ITEMS), new Set());
        const mesh = rows.find(r => r.node.name === 'Bantha#0');

        assert.equal(mesh?.expandable, false);
    });
});

describe('selectionAfterClick', () => {
    const rows = visibleTreeRows(buildTree(ITEMS), new Set());
    const plain = { ctrl: false, shift: false };

    it('replaces the selection on a plain click', () => {
        const after = selectionAfterClick(rows, new Set(['b:0', 'b:1']), 'b:0', 'm:c', plain);

        assert.deepEqual([...after.selected], ['m:c']);
        assert.equal(after.anchor, 'm:c');
    });

    it('clears the selection when the only selected row is clicked again', () => {
        // Selecting draws a box in the viewport, so there has to be a way to stop drawing one.
        // Ctrl-click already toggled, but nobody finds that, and the row you want to let go of is
        // the row under the pointer.
        const after = selectionAfterClick(rows, new Set(['m:c']), 'm:c', 'm:c', plain);

        assert.deepEqual([...after.selected], []);
    });

    it('collapses a MULTIPLE selection onto the clicked row rather than clearing it', () => {
        // With several picked, a plain click means "just this one" - the ordinary list behaviour.
        // Only the last one standing lets go.
        const after = selectionAfterClick(rows, new Set(['b:0', 'm:c']), 'b:0', 'm:c', plain);

        assert.deepEqual([...after.selected], ['m:c']);
    });

    it('adds and removes one with ctrl', () => {
        const added = selectionAfterClick(
            rows, new Set(['b:0']), 'b:0', 'm:a', { ctrl: true, shift: false });
        assert.deepEqual([...added.selected].sort(), ['b:0', 'm:a']);

        const removed = selectionAfterClick(
            rows, added.selected, 'm:a', 'b:0', { ctrl: true, shift: false });
        assert.deepEqual([...removed.selected], ['m:a']);
    });

    it('takes the run between the anchor and the click with shift', () => {
        // Over the rows AS DRAWN, so it follows what the reader can actually see. Child bones sort
        // before a bone's own geometry, so the drawn order is Root, B_Body, B_Head, Head#0,
        // p_smoke, Bantha#0, Bantha_Skirt#0 - and the run from the root to the last row is all of
        // them, whatever order the items were declared in.
        const after = selectionAfterClick(
            rows, new Set(['b:0']), 'b:0', 'm:b', { ctrl: false, shift: true });

        assert.deepEqual([...after.selected].sort(), rows.map(r => r.node.id).sort());
    });

    it('takes a partial run when the click is part-way down', () => {
        const after = selectionAfterClick(
            rows, new Set(), 'b:1', 'm:c', { ctrl: false, shift: true });

        assert.deepEqual([...after.selected], ['b:1', 'b:2', 'm:c']);
    });

    it('keeps the anchor where it was through a shift-click', () => {
        const after = selectionAfterClick(
            rows, new Set(['b:1']), 'b:1', 'm:c', { ctrl: false, shift: true });

        assert.equal(after.anchor, 'b:1');
    });

    it('falls back to a plain click when there is no anchor to extend from', () => {
        const after = selectionAfterClick(
            rows, new Set(), null, 'm:a', { ctrl: false, shift: true });

        assert.deepEqual([...after.selected], ['m:a']);
    });
});

describe('toggleTargets', () => {
    it('moves the whole selection when the clicked row is part of it', () => {
        assert.deepEqual(
            toggleTargets(new Set(['a', 'b', 'c']), 'b').sort(), ['a', 'b', 'c']);
    });

    it('moves only the clicked row when it is not selected', () => {
        // Otherwise ticking one box quietly acts on things scrolled far out of view.
        assert.deepEqual(toggleTargets(new Set(['a', 'b']), 'z'), ['z']);
    });
});

describe('a merged mesh/bone row', () => {
    /**
     * A mesh's origin IS a bone and the ALO gives them the same name, so they are ONE row - and the
     * row is anchored on the BONE's id, which is what its children already name and what survives a
     * reload. It used to be anchored on the mesh and carry the bone id as an ALIAS, which cost
     * three separate defects: a hull labelled COLLISION, a model that blanked, and checkboxes that
     * did nothing.
     */
    const MERGED: TreeItem[] = [
        item('b:0', 'bone', 'Root', null),
        { ...item('b:1', 'mesh', 'Bantha', 'b:0') },
        item('b:2', 'bone', 'B_Head', 'b:1'),
        item('particle:s#0', 'particle', 'p_smoke', 'b:1'),
    ];

    it('keeps the children that name the bone it merged', () => {
        const [root] = buildTree(MERGED);
        const merged = root.children[0];

        assert.equal(merged.name, 'Bantha');
        assert.deepEqual(merged.children.map(c => c.name), ['B_Head', 'p_smoke']);
    });

    it('does not strand those children as roots', () => {
        assert.equal(buildTree(MERGED).length, 1);
    });
});

describe('defaultCollapsed', () => {
    /**
     * Most of a tree is skeleton nobody opened it to read: 50 of the Star Destroyer's 74 rows are
     * bones carrying no geometry and no effect, and 45 of the rancor's 58. Collapsed rather than
     * hidden - the structure is still there for whoever wants it, and one click reveals it.
     */
    const ROWS: TreeItem[] = [
        item('b:0', 'bone', 'Root', null),
        item('b:1', 'bone', 'HP_F-L_Bone', 'b:0'),
        item('b:2', 'bone', 'FP_F-L_00', 'b:1'),
        item('b:3', 'bone', 'Engines', 'b:0'),
        item('mesh:3:0', 'mesh', 'engine_glow', 'b:3'),
    ];

    it('collapses a limb that carries nothing', () => {
        assert.deepEqual([...defaultCollapsed(buildTree(ROWS))], ['b:1']);
    });

    it('leaves a limb that leads to geometry open', () => {
        assert.equal(defaultCollapsed(buildTree(ROWS)).has('b:3'), false);
    });

    it('never collapses the root, which would hide the whole model', () => {
        assert.equal(defaultCollapsed(buildTree(ROWS)).has('b:0'), false);
    });

    /** A leaf has nothing to collapse, and a twisty on one is a lie. */
    it('does not collapse a childless bone', () => {
        assert.equal(defaultCollapsed(buildTree(ROWS)).has('b:2'), false);
    });

    it('collapses nothing in a tree that is all geometry', () => {
        const flat: TreeItem[] = [
            item('b:0', 'bone', 'Root', null),
            item('mesh:0:0', 'mesh', 'hull', 'b:0'),
        ];

        assert.equal(defaultCollapsed(buildTree(flat)).size, 0);
    });
});

describe('withDescendants', () => {
    const roots = buildTree(ITEMS);

    it('takes everything under the row, so hiding a limb hides the limb', () => {
        assert.deepEqual(withDescendants(roots, ['b:2']).sort(), ['b:2', 'e:x', 'm:c']);
    });

    it('takes a leaf on its own', () => {
        assert.deepEqual(withDescendants(roots, ['m:a']), ['m:a']);
    });

    it('takes the whole tree from the root', () => {
        assert.equal(withDescendants(roots, ['b:0']).length, ITEMS.length);
    });

    it('merges overlapping subtrees rather than repeating rows', () => {
        assert.deepEqual(withDescendants(roots, ['b:1', 'b:2']).sort(),
            ['b:1', 'b:2', 'e:x', 'm:c']);
    });

    it('follows a merged row, which is the bone its children name', () => {
        const merged: TreeItem[] = [
            item('b:1', 'mesh', 'Bantha', null),
            item('b:2', 'bone', 'Child', 'b:1'),
        ];

        assert.deepEqual(withDescendants(buildTree(merged), ['b:1']).sort(), ['b:1', 'b:2']);
    });

    it('keeps an id that is not in the tree at all', () => {
        // Filtered out of view is not the same as not there; the toggle still has to reach it.
        assert.deepEqual(withDescendants(roots, ['gone']), ['gone']);
    });
});

describe('meshPlacement', () => {
    const roots = new Set([0]);
    const named = (bone: string, mesh: string) => ({ bone, mesh });

    /**
     * `Alttest.alo` is the worked case: every one of its seven sub-meshes is written as a node that
     * is ALSO bone 1..7, so placing them by ancestor emitted fourteen rows - a bone row and a mesh
     * row for each, identically named, sitting in different parts of the tree. Only the mesh row's
     * checkbox did anything, which is what "some checkboxes cannot be toggled" was.
     */
    it('merges a mesh that shares its bone`s name', () => {
        assert.deepEqual(meshPlacement(3, roots, named('Teapot_ALT1', 'Teapot_ALT1')), { merge: 3 });
    });

    it('matches the name without regard to case', () => {
        assert.deepEqual(meshPlacement(3, roots, named('OilTank', 'oiltank')), { merge: 3 });
    });

    /**
     * `Rv_nebulonb.alo` is the case. Its `COLLISION` mesh rides bone 5, `Nebulon_parent` - the bone
     * every engine and hardpoint is attached to - so merging on co-location alone relabelled that whole
     * branch `COLLISION`, and the tree claimed the ship's structure was parented to its collision
     * hull. Sharing a node is the EXPORTER's doing: it writes one node per bone and puts the first
     * mesh attached to that bone on it. Sharing a NAME is the model author's, and only that means
     * the two are one thing.
     */
    it('does not merge a mesh that merely rides the bone under another name', () => {
        assert.deepEqual(
            meshPlacement(5, roots, named('Nebulon_parent', 'COLLISION')), { childOf: 5 });
    });

    it('lists a differently named mesh under its bone', () => {
        // `Ai_rancor`'s `Crusher` rides `B_Foot_L`; the bone keeps its name and its toe bones.
        assert.deepEqual(meshPlacement(38, roots, named('B_Foot_L', 'Crusher')), { childOf: 38 });
    });

    /**
     * The root is the model's own origin, not any mesh's, and it is where every skinned mesh lands.
     * Merging there labelled the whole model's root row after whichever mesh loaded first.
     */
    it('never merges into a root, even on an exact name match', () => {
        assert.deepEqual(meshPlacement(0, roots, named('Root', 'Root')), { childOf: 0 });
    });
});

/**
 * A particle proxy is one thing wearing three hats: a bone, a marker mesh the file marks hidden,
 * and the effect itself, all carrying the same name. Left as separate rows, the effect sat BELOW
 * its own bone in the chain, so the AT-AT's idle clip - which keys every effect proxy off, exactly
 * as it should - vetoed each effect with `ancestor:p_atat_die`, an ancestor of the same name as the
 * row. Ticking the effect did nothing; the reader had to find the identically named bone row above
 * it and tick that instead. Merged, the reader's word sits above the clip's where it belongs.
 */
describe('effectPlacement', () => {
    const roots = new Set([0]);
    const named = (bone: string, effect: string) => ({ bone, effect });

    it('merges an effect into the proxy bone that carries its name', () => {
        assert.deepEqual(
            effectPlacement(19, roots, named('p_explosion_empire_atat00',
                'p_explosion_empire_atat00')), { merge: 19 });
    });

    /** The AT-AT spells the bone `P_ATAT_Die` and the system `p_atat_die`. */
    it('matches the name without regard to case', () => {
        assert.deepEqual(effectPlacement(18, roots, named('P_ATAT_Die', 'p_atat_die')),
            { merge: 18 });
    });

    /**
     * An engine wash is attached to the engine block, which is geometry in its own right with its own
     * name. Folding the effect into that row would put one checkbox on a hull mesh and a plume.
     */
    it('lists an effect under a bone that is not its proxy', () => {
        assert.deepEqual(
            effectPlacement(7, roots, named('Nebulon_engines', 'pe_nebulonengines')),
            { childOf: 7 });
    });

    it('never merges into a root', () => {
        assert.deepEqual(effectPlacement(0, roots, named('Root', 'Root')), { childOf: 0 });
    });
});

describe('a bone row id', () => {
    it('round-trips the skeleton index it carries', () => {
        assert.equal(boneRowId(4), 'bone:4');
        assert.equal(boneIndexOfRow(boneRowId(4)), 4);
        assert.equal(boneIndexOfRow(boneRowId(0)), 0);
    });

    it('is nothing for a row that is not a bone', () => {
        // A mesh row and a merged proxy row both reach the same click handler. Reading `bone:` off
        // one of those with a blind slice gave `NaN`, which then selected nothing and lit no label.
        assert.equal(boneIndexOfRow('mesh:4:1'), null);
        assert.equal(boneIndexOfRow('bone:'), null);
        assert.equal(boneIndexOfRow('bone:x'), null);
        assert.equal(boneIndexOfRow(''), null);
    });
});

describe('toggleSelected', () => {
    /**
     * The rule the fire-bone buttons were missing.
     *
     * They replaced the selection outright, so picking MuzzleA_00 and then MuzzleA_01 left only the
     * second one boxed - a weapon with two fire points could never be seen as a pair, which is the
     * one thing looking at two fire points is for.
     */
    it('adds to what is already picked', () => {
        assert.deepEqual([...toggleSelected(new Set(['a']), 'b')].sort(), ['a', 'b']);
    });

    it('takes one back out without disturbing the rest', () => {
        assert.deepEqual([...toggleSelected(new Set(['a', 'b']), 'a')], ['b']);
    });

    it('picks the first one from nothing', () => {
        assert.deepEqual([...toggleSelected(new Set(), 'a')], ['a']);
    });

    it('empties when the last one is taken back out', () => {
        assert.deepEqual([...toggleSelected(new Set(['a']), 'a')], []);
    });

    it('does not mutate what it was given', () => {
        const before = new Set(['a']);
        toggleSelected(before, 'b');

        assert.deepEqual([...before], ['a']);
    });
});
