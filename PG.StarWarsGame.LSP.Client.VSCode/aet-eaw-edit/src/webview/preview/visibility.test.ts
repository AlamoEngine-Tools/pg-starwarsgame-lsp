// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
    becauseText,
    clearedForPlayback,
    deathClip,
    effectState,
    fileState,
    resolveRow,
    restingClip,
    ruleState,
    type RowFacts,
    type RowOverride,
    setRow,
    subtreeHiddenByReader,
    toggleRow,
} from "./visibility";

/** A part every switch shows: the file draws it, no clip speaks, and both levels include it. */
const facts = (over: Partial<RowFacts> = {}): RowFacts => ({
    state: fileState(true),
    alt: true,
    lod: true,
    masters: [],
    ...over,
});

describe("resolveRow, the four switches", () => {
    it("draws a part every switch shows, and says the file decided", () => {
        assert.deepEqual(resolveRow(facts()), {
            visible: true,
            decidedBy: "file",
            because: "file",
            authored: true,
        });
    });

    it("hides what the file hides, and says it is the file and not the level", () => {
        assert.deepEqual(resolveRow(facts({ state: fileState(false) })), {
            visible: false,
            decidedBy: "file",
            because: "file",
            authored: false,
        });
    });

    it("names the damage level and the detail level apart", () => {
        assert.equal(resolveRow(facts({ alt: false })).because, "alt");
        assert.equal(resolveRow(facts({ lod: false })).because, "lod");
    });

    it("hides what the playing clip hides on the part's own bone", () => {
        assert.deepEqual(
            resolveRow(facts({ animation: { shown: false, source: "clip" } })),
            {
                visible: false,
                decidedBy: "animation",
                because: "animation",
                authored: false,
            },
        );
    });

    it("never lets a clip show what the file hides", () => {
        // Four independent switches, drawn only when all four say shown. A clip keying a bone on
        // does not draw a marker mesh the file hides on it.
        assert.deepEqual(
            resolveRow(
                facts({
                    state: fileState(false),
                    animation: { shown: true, source: "clip" },
                }),
            ),
            {
                visible: false,
                decidedBy: "file",
                because: "file",
                authored: false,
            },
        );
    });

    it("never lets a clip show a part of another damage or detail level", () => {
        const clip = { shown: true, source: "clip" } as const;

        assert.equal(
            resolveRow(facts({ animation: clip, alt: false })).visible,
            false,
        );
        assert.equal(
            resolveRow(facts({ animation: clip, lod: false })).visible,
            false,
        );
    });

    it("says the clip decided a part it shows", () => {
        assert.equal(
            resolveRow(facts({ animation: { shown: true, source: "clip" } }))
                .because,
            "animation",
        );
    });

    it("names the idle a unit stands in apart from a clip being played", () => {
        assert.equal(
            resolveRow(facts({ animation: { shown: false, source: "idle" } }))
                .because,
            "idle",
        );
    });

    it("lets a rule of the game show what the file hides, and says so", () => {
        // The state switch is ONE bit with many writers: the file writes it at load, a rule writes
        // over it later, and the last write is what the part has.
        assert.deepEqual(resolveRow(facts({ state: ruleState(true) })), {
            visible: true,
            decidedBy: "rules",
            because: "rules",
            authored: true,
        });
        assert.equal(
            resolveRow(facts({ state: ruleState(false) })).because,
            "rules",
        );
    });

    it("still lets the levels and the clip hide what a rule shows", () => {
        assert.equal(
            resolveRow(facts({ state: ruleState(true), alt: false })).visible,
            false,
        );
        assert.equal(
            resolveRow(
                facts({
                    state: ruleState(true),
                    animation: { shown: false, source: "clip" },
                }),
            ).visible,
            false,
        );
    });

    it("never draws geometry that is no surface, such as a shadow volume", () => {
        assert.equal(
            resolveRow(facts({ nonVisual: true })).because,
            "archetype",
        );
    });

    it("lets the reader beat every switch, in both directions", () => {
        assert.deepEqual(
            resolveRow(
                facts({
                    state: fileState(false),
                    alt: false,
                    nonVisual: true,
                    override: "shown",
                }),
            ),
            {
                visible: true,
                decidedBy: "you",
                because: "you",
                authored: false,
            },
        );
        assert.deepEqual(resolveRow(facts({ override: "hidden" })), {
            visible: false,
            decidedBy: "you",
            because: "you",
            authored: true,
        });
    });
});

describe("resolveRow, the vetoes", () => {
    it("lets a master toggle veto everything below it", () => {
        // A master switch is a master switch: with effects off, no per-row tick puts one back.
        assert.deepEqual(
            resolveRow(
                facts({
                    override: "shown",
                    masters: [{ id: "effects", on: false }],
                }),
            ),
            {
                visible: false,
                decidedBy: "master",
                because: "master:effects",
                authored: false,
            },
        );
    });

    it("names WHICH master vetoed it", () => {
        const resolved = resolveRow(
            facts({
                masters: [
                    { id: "effects", on: true },
                    { id: "hardpoints", on: false },
                ],
            }),
        );

        assert.equal(resolved.because, "master:hardpoints");
    });

    it("a master that is ON only declines to veto - it forces nothing on", () => {
        assert.equal(
            resolveRow(
                facts({ alt: false, masters: [{ id: "effects", on: true }] }),
            ).because,
            "alt",
        );
    });

    it("lets a row the reader switched off above it veto the row", () => {
        // What the tree means: unticking an arm takes the hand off screen. The model is not the
        // reason, so the row is still one the model draws.
        assert.deepEqual(
            resolveRow(facts({ override: "shown", ancestorHidden: "Root" })),
            {
                visible: false,
                decidedBy: "ancestor",
                because: "ancestor:Root",
                authored: true,
            },
        );
    });

    it("puts a master above an ancestor, since it is the wider statement", () => {
        const resolved = resolveRow(
            facts({
                ancestorHidden: "Root",
                masters: [{ id: "effects", on: false }],
            }),
        );

        assert.equal(resolved.because, "master:effects");
    });
});

describe("subtreeHiddenByReader", () => {
    // No switch of the game is inherited: a part is not hidden because a bone above its own is.
    // Only the reader's own tick on a row reaches the rows under it.
    it("is the reader's word on the row or on a row above it, and nothing else", () => {
        assert.equal(subtreeHiddenByReader(facts()), false);
        assert.equal(
            subtreeHiddenByReader(
                facts({
                    state: fileState(false),
                    animation: { shown: false, source: "clip" },
                    alt: false,
                }),
            ),
            false,
        );
        assert.equal(
            subtreeHiddenByReader(facts({ override: "hidden" })),
            true,
        );
        assert.equal(
            subtreeHiddenByReader(facts({ ancestorHidden: "Root" })),
            true,
        );
        assert.equal(
            subtreeHiddenByReader(facts({ override: "shown" })),
            false,
        );
    });
});

describe("effectState", () => {
    it("is the file's write when the rules agree with the proxy's own flag", () => {
        assert.deepEqual(effectState(true, true), fileState(true));
        assert.deepEqual(effectState(false, false), fileState(false));
    });

    it("is the rules' write when they say otherwise", () => {
        assert.deepEqual(effectState(true, false), ruleState(false));
        assert.deepEqual(effectState(false, true), ruleState(true));
    });
});

describe("toggleRow", () => {
    // `authored` is what the model does with the row. These cases are all about a row the model
    // does NOT draw as asked, which is where the reader's word is a real statement and is stored -
    // the agreeing case is `setRow`'s, below.
    const changes = (rowEffective: boolean) =>
        toggleRow("bone:3", ["bone:4", "mesh:x"], rowEffective, rowEffective);

    it("hides the row and everything under it", () => {
        assert.deepEqual(changes(true), [
            { row: "bone:3", override: "hidden" },
            { row: "bone:4", override: "hidden" },
            { row: "mesh:x", override: "hidden" },
        ]);
    });

    it("forces the row itself ON when showing it", () => {
        // Not "clear": a shadow volume is gated off, and it is exactly the row worth ticking.
        // Clearing would leave it invisible and the tick would appear to do nothing.
        assert.equal(changes(false)[0].override, "shown");
    });

    it("CLEARS its descendants instead of forcing them on", () => {
        // Switching a bone back on must not drag its collision hull into view with it.
        assert.deepEqual(changes(false).slice(1), [
            { row: "bone:4", override: null },
            { row: "mesh:x", override: null },
        ]);
    });

    it("handles a row with nothing under it", () => {
        assert.deepEqual(toggleRow("mesh:x", [], true, true), [
            { row: "mesh:x", override: "hidden" },
        ]);
    });

    it("takes what the reader can SEE as the thing to flip", () => {
        // The defect this replaces: a merged bone/mesh row reported the mesh's layer bit and wrote
        // the bone's subtree, so the tick never changed and every further click hid it again.
        const shown: RowOverride | null = toggleRow("r", [], false, false)[0]
            .override;

        assert.equal(shown, "shown");
    });
});

describe("a clip playing", () => {
    it("hands the model back to itself", () => {
        // Every clip resets the pose each time it runs, and the visibilities are part of that pose.
        // A mesh switched off by hand three minutes ago would answer "does my animation work" wrong.
        assert.equal(clearedForPlayback().size, 0);
    });

    it("still lets the effects master hide the effects", () => {
        // The one thing worth keeping switched off while watching a clip, because particles are
        // what obstructs the view of the thing being checked. A master is not a statement about the
        // model, so a reset does not touch it.
        assert.deepEqual(
            resolveRow(
                facts({
                    animation: { shown: true, source: "clip" },
                    masters: [{ id: "effects", on: false }],
                }),
            ),
            {
                visible: false,
                decidedBy: "master",
                because: "master:effects",
                authored: false,
            },
        );
    });
});

describe("what the MODEL says, ignoring the reader", () => {
    // The tree renders a row in italics when the model itself does not draw it, so someone looking
    // at a ticked row that the file marks hidden can see that they are the reason it is on screen.
    // Answering that needs the chain run a second way: with the reader's word taken back out.
    it("is what the row would do if the reader had said nothing", () => {
        const resolved = resolveRow(facts({ lod: false, override: "shown" }));

        assert.equal(resolved.visible, true);
        assert.equal(resolved.authored, false);
    });

    it("is not simply the inverse of the override", () => {
        // Hiding a mesh the model draws leaves it AUTHORED visible - the row is unticked, upright.
        // Only the model's own silence about a row makes it italic.
        const resolved = resolveRow(facts({ override: "hidden" }));

        assert.equal(resolved.visible, false);
        assert.equal(resolved.authored, true);
    });

    it("lets a playing clip speak for the model", () => {
        // A clip IS the model as its author animated it, so a frame that hides a bone is the model
        // hiding it - even though the file's own flag says otherwise.
        assert.equal(
            resolveRow(facts({ animation: { shown: false, source: "clip" } }))
                .authored,
            false,
        );
    });
});

describe("setRow, which says what it WANTS rather than what it sees", () => {
    // The distinction that broke the tree: a checkbox's change event carries the state the reader
    // is asking FOR, and the Show and Hide buttons say what they will do. Feeding either into a
    // function that expects the CURRENT state inverts every one of them - the tick appeared dead
    // because it kept setting the row to what it already was, and "Hide" showed things.
    // `authored` is what the MODEL would do with this row if the reader had never touched it. It
    // decides whether the reader's word needs storing at all - see below.
    const changes = (visible: boolean, authored = true) =>
        setRow("bone:3", ["bone:4", "mesh:x"], visible, authored);

    it("hides the row and everything under it when asked to hide", () => {
        assert.deepEqual(changes(false), [
            { row: "bone:3", override: "hidden" },
            { row: "bone:4", override: "hidden" },
            { row: "mesh:x", override: "hidden" },
        ]);
    });

    it("clears its subtree when asked to show", () => {
        // Forcing the children on would drag every collision hull and shadow volume under that bone
        // into view, since those are gated off rather than absent.
        assert.deepEqual(changes(true).slice(1), [
            { row: "bone:4", override: null },
            { row: "mesh:x", override: null },
        ]);
    });

    it("stores the reader`s word only where it DISAGREES with the model", () => {
        // A shadow volume is gated off, so asking for it is a real statement and has to be stored -
        // clearing it would leave it invisible, which is the row worth ticking in the first place.
        assert.deepEqual(changes(true, false)[0], {
            row: "bone:3",
            override: "shown",
        });
        assert.deepEqual(changes(false, true)[0], {
            row: "bone:3",
            override: "hidden",
        });
    });

    it("GIVES THE ROW BACK to the model when the two agree", () => {
        // The way back, and the reason the tree's eye has a third state at all. Hiding a mesh the
        // model draws and then showing it again used to leave a standing `shown` override - which
        // outranks the animation, the level and the file for good, and is what let one tick on an
        // ability's effect row break that ability's switch permanently. Asking for what the model
        // already does is not a statement; it is the absence of one.
        assert.deepEqual(changes(true, true)[0], {
            row: "bone:3",
            override: null,
        });
        assert.deepEqual(changes(false, false)[0], {
            row: "bone:3",
            override: null,
        });
    });

    it("is what toggleRow does, read the other way round", () => {
        // toggleRow takes what the reader can SEE and flips it. The two must not drift apart.
        assert.deepEqual(
            toggleRow("bone:3", ["bone:4"], true, false),
            setRow("bone:3", ["bone:4"], false, false),
        );
        assert.deepEqual(
            toggleRow("bone:3", ["bone:4"], false, true),
            setRow("bone:3", ["bone:4"], true, true),
        );
    });
});

describe("restingClip", () => {
    it("picks the idle clip", () => {
        assert.equal(
            restingClip([
                "ev_at-at_attack_00",
                "ev_at-at_idle_00",
                "ev_at-at_move_00",
            ]),
            "ev_at-at_idle_00",
        );
    });

    it("has nothing to say when the model ships no idle", () => {
        assert.equal(
            restingClip(["ev_at-at_attack_00", "ev_at-at_die_00"]),
            undefined,
        );
    });

    /**
     * An ATTACK idle is not a resting pose, and Yoda is the model that proves it.
     *
     * He carries a cane at rest and draws the lightsaber to fight, and his clips say so: `idle_00`
     * hides the `saber` bone and shows `stick`, while `attackidle_00` does the opposite. Matching
     * any clip that merely CONTAINS "idle" picked `attackidle_00` - it sorts first - so the model
     * opened with the blade out at his hip and the cane gone, on every frame, for every reader.
     *
     * The same trap `deathClip` beside this already avoids, and for the same reason: `diehard` is
     * not a death and `attackidle` is not an idle. The word has to stand on its own.
     */
    it("does not mistake an attack idle for a resting pose", () => {
        const yoda = [
            "Ri_yoda_attack_00",
            "Ri_yoda_attackidle_00",
            "Ri_yoda_idle_00",
            "Ri_yoda_walk_00",
        ];

        assert.equal(restingClip(yoda), "Ri_yoda_idle_00");
    });

    it("still takes an idle that leads or trails the name", () => {
        assert.equal(restingClip(["idle_00"]), "idle_00");
        assert.equal(restingClip(["ev_at-at_idle"]), "ev_at-at_idle");
    });

    /**
     * A qualified idle is still a rest when it is all the model has.
     *
     * Measured over the 2408 shipped clips: every qualified idle is a prefix - attackidle 108,
     * crouchidle 23, flyidle 4, flylandidle 4 - and a flier whose only rest is `flyidle` has to
     * rest on something. They lose to a plain idle; they do not stop counting.
     */
    it("falls back to a qualified idle when the model ships no plain one", () => {
        assert.equal(
            restingClip(["ev_fighter_attack_00", "ev_fighter_flyidle_00"]),
            "ev_fighter_flyidle_00",
        );
        assert.equal(
            restingClip(["ev_trooper_attackidle_00"]),
            "ev_trooper_attackidle_00",
        );
    });

    it("takes the same one every time when several match", () => {
        // Deterministic, or the model would open differently on different days.
        const clips = ["b_idle_01", "a_idle_00"];

        assert.equal(restingClip(clips), restingClip([...clips].reverse()));
    });

    it("does not mistake a name that merely contains the word", () => {
        assert.equal(
            restingClip(["ev_probe_idlewalk_00"]),
            "ev_probe_idlewalk_00",
        );
    });
});

describe("deathClip", () => {
    it("picks the clip that says die", () => {
        assert.equal(
            deathClip([
                "rv_moncalcruiser_d_idle_00",
                "rv_moncalcruiser_d_die_00",
            ]),
            "rv_moncalcruiser_d_die_00",
        );
    });

    it("falls back to the only clip there is", () => {
        // The host hands the server every clip the SCENE found, so a death clone's list is not
        // guaranteed to name its own death - and having geometry play nothing is worse than
        // playing the one thing it shipped.
        assert.equal(
            deathClip(["rv_moncalcruiser_d_00"]),
            "rv_moncalcruiser_d_00",
        );
    });

    it("has nothing to play when the model ships no clip at all", () => {
        assert.equal(deathClip([]), undefined);
    });

    it("takes the same one every time when several match", () => {
        const clips = ["b_die_01", "a_die_00"];

        assert.equal(deathClip(clips), deathClip([...clips].reverse()));
    });

    it("does not mistake a name that merely contains the letters", () => {
        // `died` and `diego` both carry `die` and neither is a death clip; the word has to stand on
        // its own between separators, which is how the exporter writes an action.
        assert.equal(
            deathClip(["ev_x_diehard_00", "ev_x_attack_00"]),
            "ev_x_attack_00",
        );
    });
});

describe("becauseText", () => {
    // These are ROW tooltips: read in the second before a click, on a tree that can be a hundred
    // rows deep. One short clause each, and never the same fact twice.
    const reasons = [
        "you",
        "idle",
        "animation",
        "alt",
        "lod",
        "rules",
        "archetype",
        "file",
        "master:effects",
        "ancestor:B_Chest",
    ];

    it("says which switch decided, in a clause", () => {
        assert.equal(becauseText("you"), "Set by hand");
        // NOT the T-pose. That is the model with nothing driving the skeleton at all; this is the
        // unit's own idle clip running, and its visibility track deciding this row.
        assert.equal(becauseText("idle"), "Set by the idle animation");
        assert.equal(becauseText("animation"), "Set by the animation playing");
        assert.equal(becauseText("alt"), "Not in this damage level");
        assert.equal(becauseText("lod"), "Not in this detail level");
        assert.equal(becauseText("rules"), "Set by the game rules");
        assert.equal(becauseText("archetype"), "Not drawn as a surface");
        assert.equal(becauseText("file"), "As the model draws it");
    });

    it("names the switch or the ancestor that did it", () => {
        assert.equal(
            becauseText("master:effects"),
            "Hidden by the effects switch",
        );
        assert.equal(
            becauseText("ancestor:B_Chest"),
            "Hidden with B_Chest, above it",
        );
    });

    it("keeps every reason inside a line", () => {
        for (const because of reasons) {
            // Seven, which is what the longest of these needs. The number is a proxy for "fits the
            // line you read it on"; the offender it was written against ran to thirteen and said
            // the same thing twice.
            assert.ok(
                becauseText(because).split(" ").length <= 7,
                `${because}: ${becauseText(because)}`,
            );
        }
    });

    it("never writes an apostrophe it cannot spell", () => {
        // The house rule is ASCII-only in user-facing strings, and "the model s idle animation" is
        // what dropping one leaves behind. Rephrase instead.
        for (const because of reasons) {
            assert.doesNotMatch(becauseText(because), / s\b/);
        }
    });
});
