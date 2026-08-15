// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
    clearedForPlayback, restingClip, resolveRow, setRow, subtreeFacts, toggleRow,
    type RowFacts, type RowOverride,
} from './visibility';

const facts = (over: Partial<RowFacts> = {}): RowFacts =>
    ({ inFile: true, gated: false, masters: [], ...over });

describe('resolveRow, the chain in order', () => {
    it('draws what the file draws, and says the file decided', () => {
        assert.deepEqual(resolveRow(facts()),
            { visible: true, because: 'file', authored: true });
    });

    it('does not draw what the file marks hidden', () => {
        assert.deepEqual(resolveRow(facts({ inFile: false })),
            { visible: false, because: 'file', authored: false });
    });

    it('does not draw what the current level gates off', () => {
        assert.deepEqual(resolveRow(facts({ gated: true })),
            { visible: false, because: 'level', authored: false });
    });

    it('lets a playing clip beat the file and the level', () => {
        // The clip is the model as its author animated it, so where nobody has overridden it, it
        // decides - including switching something ON that the file marks hidden.
        assert.deepEqual(resolveRow(facts({ inFile: false, gated: true, animated: true })),
            { visible: true, because: 'animation', authored: true });
    });

    it('lets the reader beat the clip, in both directions', () => {
        assert.deepEqual(resolveRow(facts({ animated: true, override: 'hidden' })),
            { visible: false, because: 'you', authored: true });
        assert.deepEqual(resolveRow(facts({ animated: false, override: 'shown' })),
            { visible: true, because: 'you', authored: false });
    });

    it('lets the reader show something the level gates off', () => {
        // A collision hull and a shadow volume are both gated off, and both are exactly what
        // someone opens the model view to look at.
        assert.deepEqual(resolveRow(facts({ inFile: false, gated: true, override: 'shown' })),
            { visible: true, because: 'you', authored: false });
    });
});

describe('resolveRow, the vetoes', () => {
    it('lets a master toggle veto everything below it', () => {
        // A master switch is a master switch: with effects off, no per-row tick puts one back.
        assert.deepEqual(
            resolveRow(facts({ override: 'shown', masters: [{ id: 'effects', on: false }] })),
            { visible: false, because: 'master:effects', authored: false });
    });

    it('names WHICH master vetoed it', () => {
        const resolved = resolveRow(facts({
            masters: [{ id: 'effects', on: true }, { id: 'hardpoints', on: false }],
        }));

        assert.equal(resolved.because, 'master:hardpoints');
    });

    it('a master that is ON only declines to veto - it forces nothing on', () => {
        assert.deepEqual(resolveRow(facts({ gated: true, masters: [{ id: 'effects', on: true }] })),
            { visible: false, because: 'level', authored: false });
    });

    it('lets a hidden ancestor veto the row', () => {
        // What a tree means: a hand cannot be drawn while its arm is not. The ancestor here is one
        // the MODEL hides, so the row is not drawn either way - see `ancestorHiddenAuthored`.
        assert.deepEqual(
            resolveRow(facts({
                override: 'shown', ancestorHidden: 'Root', ancestorHiddenAuthored: 'Root',
            })),
            { visible: false, because: 'ancestor:Root', authored: false });
    });

    it('puts a master above an ancestor, since it is the wider statement', () => {
        const resolved = resolveRow(facts({
            ancestorHidden: 'Root', masters: [{ id: 'effects', on: false }],
        }));

        assert.equal(resolved.because, 'master:effects');
    });
});

describe('toggleRow', () => {
    const changes = (rowEffective: boolean) => toggleRow('bone:3', ['bone:4', 'mesh:x'], rowEffective);

    it('hides the row and everything under it', () => {
        assert.deepEqual(changes(true), [
            { row: 'bone:3', override: 'hidden' },
            { row: 'bone:4', override: 'hidden' },
            { row: 'mesh:x', override: 'hidden' },
        ]);
    });

    it('forces the row itself ON when showing it', () => {
        // Not "clear": a shadow volume is gated off, and it is exactly the row worth ticking.
        // Clearing would leave it invisible and the tick would appear to do nothing.
        assert.equal(changes(false)[0].override, 'shown');
    });

    it('CLEARS its descendants instead of forcing them on', () => {
        // Switching a bone back on must not drag its collision hull into view with it.
        assert.deepEqual(changes(false).slice(1), [
            { row: 'bone:4', override: null },
            { row: 'mesh:x', override: null },
        ]);
    });

    it('handles a row with nothing under it', () => {
        assert.deepEqual(toggleRow('mesh:x', [], true), [{ row: 'mesh:x', override: 'hidden' }]);
    });

    it('takes what the reader can SEE as the thing to flip', () => {
        // The defect this replaces: a merged bone/mesh row reported the mesh's layer bit and wrote
        // the bone's subtree, so the tick never changed and every further click hid it again.
        const shown: RowOverride | null = toggleRow('r', [], false)[0].override;

        assert.equal(shown, 'shown');
    });
});

describe('a clip playing', () => {
    it('hands the model back to itself', () => {
        // Every clip resets the pose each time it runs, and the visibilities are part of that pose.
        // A mesh switched off by hand three minutes ago would answer "does my animation work" wrong.
        assert.equal(clearedForPlayback().size, 0);
    });

    it('still lets the effects master hide the effects', () => {
        // The one thing worth keeping switched off while watching a clip, because particles are
        // what obstructs the view of the thing being checked. A master is not a statement about the
        // model, so a reset does not touch it.
        assert.deepEqual(
            resolveRow(facts({ animated: true, masters: [{ id: 'effects', on: false }] })),
            { visible: false, because: 'master:effects', authored: false });
    });

    it('otherwise lets the clip decide, with nothing overridden', () => {
        assert.deepEqual(resolveRow(facts({ inFile: false, gated: true, animated: true })),
            { visible: true, because: 'animation', authored: true });
    });
});

describe('what the MODEL says, ignoring the reader', () => {
    // The tree renders a row in italics when the model itself does not draw it, so someone looking
    // at a ticked row that the file marks hidden can see that they are the reason it is on screen.
    // Answering that needs the chain run a second way: with the reader's word taken back out.
    it('is what the row would do if the reader had said nothing', () => {
        const resolved = resolveRow(facts({ gated: true, override: 'shown' }));

        assert.equal(resolved.visible, true);
        assert.equal(resolved.authored, false);
    });

    it('is not simply the inverse of the override', () => {
        // Hiding a mesh the model draws leaves it AUTHORED visible - the row is unticked, upright.
        // Only the model's own silence about a row makes it italic.
        const resolved = resolveRow(facts({ override: 'hidden' }));

        assert.equal(resolved.visible, false);
        assert.equal(resolved.authored, true);
    });

    it('does not blame the model for an ancestor the READER hid', () => {
        // Unticking a bone hides everything under it, and every one of those rows then said the
        // model does not draw it - so a whole subtree went italic because of one tick. What the
        // model says about a child is what it would say if the parent had been left alone.
        const resolved = resolveRow(facts({
            ancestorHidden: 'Root', ancestorHiddenAuthored: undefined,
        }));

        assert.equal(resolved.visible, false);
        assert.equal(resolved.authored, true);
    });

    it('still blames the model when the ancestor is hidden by the model too', () => {
        const resolved = resolveRow(facts({
            ancestorHidden: 'Root', ancestorHiddenAuthored: 'Root',
        }));

        assert.equal(resolved.authored, false);
    });

    it('lets a playing clip speak for the model', () => {
        // A clip IS the model as its author animated it, so a frame that hides a bone is the model
        // hiding it - even though the file's own flag says otherwise.
        assert.equal(resolveRow(facts({ inFile: true, animated: false })).authored, false);
    });
});

describe('setRow, which says what it WANTS rather than what it sees', () => {
    // The distinction that broke the tree: a checkbox's change event carries the state the reader
    // is asking FOR, and the Show and Hide buttons say what they will do. Feeding either into a
    // function that expects the CURRENT state inverts every one of them - the tick appeared dead
    // because it kept setting the row to what it already was, and "Hide" showed things.
    const changes = (visible: boolean) => setRow('bone:3', ['bone:4', 'mesh:x'], visible);

    it('hides the row and everything under it when asked to hide', () => {
        assert.deepEqual(changes(false), [
            { row: 'bone:3', override: 'hidden' },
            { row: 'bone:4', override: 'hidden' },
            { row: 'mesh:x', override: 'hidden' },
        ]);
    });

    it('forces the row on and clears its subtree when asked to show', () => {
        assert.deepEqual(changes(true), [
            { row: 'bone:3', override: 'shown' },
            { row: 'bone:4', override: null },
            { row: 'mesh:x', override: null },
        ]);
    });

    it('is what toggleRow does, read the other way round', () => {
        // toggleRow takes what the reader can SEE and flips it. The two must not drift apart.
        assert.deepEqual(toggleRow('bone:3', ['bone:4'], true), setRow('bone:3', ['bone:4'], false));
        assert.deepEqual(toggleRow('bone:3', ['bone:4'], false), setRow('bone:3', ['bone:4'], true));
    });
});

describe('subtreeFacts, for a row that is a bone AND a mesh', () => {
    // A merged row writes two things: the mesh's own drawn flag, and the BONE's visible flag, which
    // prunes everything hanging beneath it. They must not be decided by the same facts.
    //
    // The AT-AT is the case that proved it. Its particle proxies are bones named after the effect
    // they carry - `P_fire_med01` - and each has a marker mesh of the same name that the file marks
    // hidden. Merged, the row resolved hidden from the MESH's flag and pruned the bone's subtree
    // with it, so the effect hanging off that bone was vetoed by an ancestor and could never be
    // switched on. Every fire, smoke and explosion proxy on the model was dark.
    it('drops what the file and the level say about the MESH', () => {
        const facts: RowFacts = { inFile: false, gated: true, masters: [] };

        assert.deepEqual(subtreeFacts(facts), { inFile: true, gated: false, masters: [] });
    });

    it('keeps everything that applies to the whole subtree', () => {
        // Hiding a bone by hand DOES take its children - that is what a tree means - and so does a
        // master switch, a hidden ancestor and a clip that hides the bone.
        const facts: RowFacts = {
            inFile: false,
            gated: true,
            override: 'hidden',
            animated: false,
            ancestorHidden: 'Root',
            ancestorHiddenAuthored: 'Root',
            masters: [{ id: 'effects', on: false }],
        };

        const subtree = subtreeFacts(facts);

        assert.equal(subtree.override, 'hidden');
        assert.equal(subtree.animated, false);
        assert.equal(subtree.ancestorHidden, 'Root');
        assert.deepEqual(subtree.masters, [{ id: 'effects', on: false }]);
    });

    it('leaves a marker mesh hidden while its subtree stays live', () => {
        const facts: RowFacts = { inFile: false, gated: false, masters: [] };

        assert.equal(resolveRow(facts).visible, false);
        assert.equal(resolveRow(subtreeFacts(facts)).visible, true);
    });

    it('still lets the reader hide the whole thing', () => {
        const facts: RowFacts = { inFile: false, gated: false, override: 'hidden', masters: [] };

        assert.equal(resolveRow(subtreeFacts(facts)).visible, false);
    });
});

describe('the resting state, which is the idle clip when there is one', () => {
    // The engine never stands still: a unit is always playing something, and for the AT-AT that is
    // `ev_at-at_idle_00`, which keys both barrel muzzles off for all 81 of its frames. The model
    // FILE leaves those flash meshes unhidden precisely because the animations own that state - so
    // reading the file alone puts two muzzle flashes on screen that nobody ever sees in game.
    it('lets the idle clip set the resting state', () => {
        assert.deepEqual(resolveRow(facts({ inFile: true, resting: false })),
            { visible: false, because: 'idle', authored: false });
    });

    it('falls back to the file when no idle clip says anything', () => {
        assert.deepEqual(resolveRow(facts({ inFile: true })),
            { visible: true, because: 'file', authored: true });
    });

    it('still lets the level gate come first', () => {
        // A mesh tagged for another damage state is not part of this one, whatever idle says.
        assert.equal(resolveRow(facts({ gated: true, resting: true })).because, 'level');
    });

    it('still lets a playing clip beat it', () => {
        assert.equal(resolveRow(facts({ resting: false, animated: true })).because, 'animation');
    });

    it('carries into the subtree, being a statement about the bone', () => {
        assert.equal(subtreeFacts(facts({ resting: false })).resting, false);
    });
});

describe('restingClip', () => {
    it('picks the idle clip', () => {
        assert.equal(restingClip(['ev_at-at_attack_00', 'ev_at-at_idle_00', 'ev_at-at_move_00']),
            'ev_at-at_idle_00');
    });

    it('has nothing to say when the model ships no idle', () => {
        assert.equal(restingClip(['ev_at-at_attack_00', 'ev_at-at_die_00']), undefined);
    });

    it('takes the same one every time when several match', () => {
        // Deterministic, or the model would open differently on different days.
        const clips = ['b_idle_01', 'a_idle_00'];

        assert.equal(restingClip(clips), restingClip([...clips].reverse()));
    });

    it('does not mistake a name that merely contains the word', () => {
        assert.equal(restingClip(['ev_probe_idlewalk_00']), 'ev_probe_idlewalk_00');
    });
});
