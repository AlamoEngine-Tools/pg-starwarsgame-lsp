// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
    EncyclopediaTextStyle, GetEncyclopediaEntryResult,
} from '../protocol/encyclopedia';
import { encyclopediaNotices, noticeSeverity } from './encyclopediaNotices';

function style(fontName: string): EncyclopediaTextStyle {
    return {
        component: 'encyclopedia_text',
        fontName,
        fontPointSize: 8,
        scale: 1,
        textColor: { r: 192, g: 192, b: 192, a: 255 },
        alignment: 'left',
    };
}

function makeEntry(over: Partial<GetEncyclopediaEntryResult> = {}): GetEncyclopediaEntryResult {
    return {
        found: true,
        objectId: 'Rebel_Corvette',
        body: [],
        usedMultiplayerBody: false,
        abilities: [],
        goodAgainst: [],
        vulnerableTo: [],
        layout: {
            width: 262,
            rowHeight: 14,
            offsetX: 5,
            offsetY: 2,
            iconScale: 0.75,
            abilityIconScale: 0.66,
            backdropColor: { r: 21, g: 32, b: 73, a: 255 },
            backdropTextureName: 'E_BACKGROUND.TGA',
            factionFrameTextureNames: [],
            header: style('Arial'),
            body: style('Arial'),
            rightText: style('Arial'),
            centerText: style('Arial'),
            costText: style('Arial'),
        },
        ...over,
    };
}

describe('encyclopediaNotices', () => {
    it('reports nothing about a card that has nothing wrong with it', () => {
        assert.deepEqual(encyclopediaNotices(makeEntry(), { multiplayer: false, factionSlot: 0 }), []);
    });

    it('reports nothing at all before an entry has arrived', () => {
        assert.deepEqual(encyclopediaNotices(null, { multiplayer: false, factionSlot: 0 }), []);
    });

    it('says nothing about an object that has no encyclopedia entry', () => {
        // There is no card, so every notice below would be about a card nobody is looking at.
        const entry = makeEntry({ found: false });
        assert.deepEqual(encyclopediaNotices(entry, { multiplayer: false, factionSlot: 0 }), []);
    });

    describe('substituted fonts', () => {
        it('notes them as information, not as a warning', () => {
            // The licence is ours to work around, not the modder's to fix - a permanent yellow
            // warning on nearly every card is one people learn to ignore.
            const entry = makeEntry({
                layout: { ...makeEntry().layout, costText: style('EmpireAtWar-Bold') },
            });

            const notices = encyclopediaNotices(entry, { multiplayer: false, factionSlot: 0 });
            assert.equal(notices.length, 1);
            assert.equal(notices[0].severity, 'info');
            assert.match(notices[0].message, /EmpireAtWar-Bold/);
        });

        it('names each substituted font once, however many rows ask for it', () => {
            const base = makeEntry().layout;
            const entry = makeEntry({
                layout: {
                    ...base,
                    costText: style('EmpireAtWar-Bold'),
                    rightText: style('EmpireAtWar-Bold'),
                    centerText: style('EmpireAtWar-Medium'),
                },
            });

            const notices = encyclopediaNotices(entry, { multiplayer: false, factionSlot: 0 });
            assert.equal(notices.length, 1);
            assert.equal(notices[0].message.match(/EmpireAtWar-Bold/g)?.length, 1);
            assert.match(notices[0].message, /EmpireAtWar-Medium/);
        });

        it('says nothing when a mod re-fonts those rows to something we can draw', () => {
            const entry = makeEntry({
                layout: { ...makeEntry().layout, costText: style('Verdana') },
            });
            assert.deepEqual(encyclopediaNotices(entry, { multiplayer: false, factionSlot: 0 }), []);
        });
    });

    describe('ship names', () => {
        it('warns when the wired-up name file is missing', () => {
            const entry = makeEntry({
                shipNames: { sourcePath: 'Data\\Text\\Names.txt', fileFound: false, names: [] },
            });

            const notices = encyclopediaNotices(entry, { multiplayer: false, factionSlot: 0 });
            assert.equal(notices.length, 1);
            assert.equal(notices[0].severity, 'warning');
            assert.match(notices[0].message, /Names\.txt/);
        });

        it('warns separately when the file is there but no names came out of it', () => {
            // A different fault with a different fix: the encoding, not the path.
            const entry = makeEntry({
                shipNames: { sourcePath: 'Data\\Text\\Names.txt', fileFound: true, names: [] },
            });

            const notices = encyclopediaNotices(entry, { multiplayer: false, factionSlot: 0 });
            assert.equal(notices.length, 1);
            assert.equal(notices[0].severity, 'warning');
            assert.match(notices[0].message, /UTF-16/);
        });

        it('explains the substitution as a note when the pool read fine', () => {
            // Not a fault - it explains why the card shows a name where every other object shows a
            // class, which is surprising the first time you meet it.
            const entry = makeEntry({
                shipNames: {
                    sourcePath: 'Data\\Text\\Names.txt', fileFound: true, names: ['Devastator'],
                },
            });

            const notices = encyclopediaNotices(entry, { multiplayer: false, factionSlot: 0 });
            assert.equal(notices.length, 1);
            assert.equal(notices[0].severity, 'info');
            assert.match(notices[0].message, /individual name/);
            // The note names the file too, the way both ship-name warnings do - where the pool came
            // from is the first thing you need when the names are not the ones you expected.
            assert.match(notices[0].message, /Data\\Text\\Names\.txt/);
        });

        it('does not claim a name is drawn when the pool is empty', () => {
            // The card has fallen back to the class line, so "draws an individual name instead of
            // its class" would be flatly untrue. The empty-pool warning is the only thing to say.
            const entry = makeEntry({
                shipNames: { sourcePath: 'Data\\Text\\Names.txt', fileFound: true, names: [] },
            });

            const notices = encyclopediaNotices(entry, { multiplayer: false, factionSlot: 0 });
            assert.equal(notices.length, 1);
            assert.equal(notices[0].severity, 'warning');
        });
    });

    describe('faction frames', () => {
        const frames = (over: { slot: number; hasArt: boolean }[]) => ({
            factionFrames: over.map(f => ({
                slot: f.slot,
                textureName: `i_tooltip_frame_${f.slot}.tga`,
                image: f.hasArt ? { dataUri: 'data:image/png;base64,AA', width: 4, height: 4 } : null,
            })),
        });

        it('warns about the SELECTED slot having no artwork', () => {
            const entry = makeEntry({
                chrome: frames([{ slot: 0, hasArt: true }, { slot: 1, hasArt: false }]),
            });

            const notices = encyclopediaNotices(entry, { multiplayer: false, factionSlot: 1 });
            assert.equal(notices.length, 1);
            assert.equal(notices[0].severity, 'warning');
            assert.match(notices[0].message, /i_tooltip_frame_1\.tga/);
        });

        it('stays quiet about a slot the user is not looking at', () => {
            // The card draws one frame; an unselected slot's missing art changes nothing on screen.
            const entry = makeEntry({
                chrome: frames([{ slot: 0, hasArt: true }, { slot: 1, hasArt: false }]),
            });
            assert.deepEqual(encyclopediaNotices(entry, { multiplayer: false, factionSlot: 0 }), []);
        });
    });

    describe('the multiplayer body', () => {
        it('notes that the object has none, but only once it was asked for', () => {
            const entry = makeEntry({ usedMultiplayerBody: false });

            assert.deepEqual(encyclopediaNotices(entry, { multiplayer: false, factionSlot: 0 }), []);

            const asked = encyclopediaNotices(entry, { multiplayer: true, factionSlot: 0 });
            assert.equal(asked.length, 1);
            assert.equal(asked[0].severity, 'info');
            assert.match(asked[0].message, /MP_Encyclopedia_Text/);
        });

        it('says nothing when the multiplayer body was actually used', () => {
            const entry = makeEntry({ usedMultiplayerBody: true });
            assert.deepEqual(encyclopediaNotices(entry, { multiplayer: true, factionSlot: 0 }), []);
        });
    });

    describe('the portrait', () => {
        const icon = (over: Partial<GetEncyclopediaEntryResult['icon'] & object>) => ({
            name: 'I_REBEL_CORVETTE.TGA',
            dataUri: 'data:image/png;base64,AA',
            source: 'Baseline' as const,
            isMegaTextureStale: false,
            width: 50,
            height: 50,
            ...over,
        });

        it('warns when the object names an icon nothing could supply', () => {
            const entry = makeEntry({ icon: icon({ source: 'Fallback' }) });

            const notices = encyclopediaNotices(entry, { multiplayer: false, factionSlot: 0 });
            assert.equal(notices.length, 1);
            assert.equal(notices[0].severity, 'warning');
            assert.match(notices[0].message, /I_REBEL_CORVETTE\.TGA/);
        });

        it('warns when the icon is drawn but not yet repacked', () => {
            const entry = makeEntry({
                icon: icon({ source: 'LooseSource', isMegaTextureStale: true }),
            });

            const notices = encyclopediaNotices(entry, { multiplayer: false, factionSlot: 0 });
            assert.equal(notices.length, 1);
            assert.equal(notices[0].severity, 'warning');
            assert.match(notices[0].message, /mega texture/i);
        });

        it('says nothing about an icon that resolved normally', () => {
            const entry = makeEntry({ icon: icon({}) });
            assert.deepEqual(encyclopediaNotices(entry, { multiplayer: false, factionSlot: 0 }), []);
        });
    });

    it('puts warnings before notes, so the worst of it reads first', () => {
        const entry = makeEntry({
            layout: { ...makeEntry().layout, costText: style('EmpireAtWar-Bold') },
            shipNames: { sourcePath: 'Names.txt', fileFound: false, names: [] },
        });

        const notices = encyclopediaNotices(entry, { multiplayer: false, factionSlot: 0 });
        assert.deepEqual(notices.map(n => n.severity), ['warning', 'info']);
    });
});

describe('noticeSeverity', () => {
    it('is ok when there is nothing to say', () => {
        assert.equal(noticeSeverity([]), 'ok');
    });

    it('is info when only notes were raised', () => {
        assert.equal(noticeSeverity([{ severity: 'info', message: 'x' }]), 'info');
    });

    it('takes the worst of a mixed list', () => {
        assert.equal(noticeSeverity([
            { severity: 'info', message: 'x' },
            { severity: 'warning', message: 'y' },
        ]), 'warning');
    });
});
