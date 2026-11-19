// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { emitter, properties, testSystem } from "./particleFixture";
import {
    ParticlePreparations,
    prepareParticleSystem,
} from "./particlePreparation";

describe("ParticlePreparations", () => {
    it("prepares two systems that differ only in the stored system name once", () => {
        const preparations = new ParticlePreparations();
        const emitters = () => [
            emitter({ name: "Spark" }),
            emitter({ name: "Smoke" }),
        ];

        const first = preparations.of(testSystem(emitters(), "P_engine_a"));
        const second = preparations.of(testSystem(emitters(), "P_engine_b"));

        assert.equal(second, first);
        assert.equal(preparations.prepared, 1);
    });

    it("answers for a system it has seen without preparing it again", () => {
        const preparations = new ParticlePreparations();
        const system = testSystem([emitter()]);

        assert.equal(preparations.of(system), preparations.of(system));
        assert.equal(preparations.prepared, 1);
    });

    it("prepares a system whose emitters differ apart", () => {
        const preparations = new ParticlePreparations();

        const first = preparations.of(testSystem([emitter({ name: "A" })]));
        const second = preparations.of(
            testSystem([
                emitter({
                    name: "A",
                    properties: properties({ particlesPerSecond: 5 }),
                }),
            ]),
        );

        assert.notEqual(second, first);
        assert.equal(preparations.prepared, 2);
    });

    it("tells a system that leaves its particles from one that does not", () => {
        const preparations = new ParticlePreparations();
        const lingering = {
            ...testSystem([emitter()]),
            leaveParticles: true,
        };

        assert.notEqual(
            preparations.of(lingering),
            preparations.of(testSystem([emitter()])),
        );
    });

    it("forgets what it prepared when cleared", () => {
        const preparations = new ParticlePreparations();
        preparations.of(testSystem([emitter()]));

        preparations.clear();
        preparations.of(testSystem([emitter()]));

        assert.equal(preparations.prepared, 1);
    });
});

describe("prepareParticleSystem", () => {
    it("says whether any emitter is born on a mesh", () => {
        const plain = prepareParticleSystem(testSystem([emitter()]));
        const onMesh = prepareParticleSystem(
            testSystem([
                emitter(),
                emitter({
                    properties: properties({ emitFromMesh: "RandomVertex" }),
                }),
            ]),
        );

        assert.equal(plain.emitsFromMesh, false);
        assert.equal(onMesh.emitsFromMesh, true);
    });

    it("orders the emitters by priority ticket, file order on ties", () => {
        const prepared = prepareParticleSystem(
            testSystem([
                emitter({ properties: properties({ priorityTicket: 2 }) }),
                emitter({ properties: properties({ priorityTicket: 0 }) }),
                emitter({ properties: properties({ priorityTicket: 2 }) }),
                emitter({ properties: properties({ priorityTicket: 1 }) }),
            ]),
        );

        assert.deepEqual(prepared.order, [1, 3, 0, 2]);
    });

    it("keeps file order for a system with no tickets", () => {
        const prepared = prepareParticleSystem(
            testSystem([emitter(), emitter(), emitter()]),
        );

        assert.deepEqual(prepared.order, [0, 1, 2]);
    });

    it("names the emitters another emitter spawns, by ticket where there are tickets", () => {
        const byPosition = prepareParticleSystem(
            testSystem([emitter({ spawnOnDeath: 1 }), emitter()]),
        );
        const byTicket = prepareParticleSystem(
            testSystem([
                emitter({
                    spawnDuringLife: 7,
                    properties: properties({ priorityTicket: 3 }),
                }),
                emitter({ properties: properties({ priorityTicket: 9 }) }),
                emitter({ properties: properties({ priorityTicket: 7 }) }),
            ]),
        );

        assert.deepEqual([...byPosition.dependent], [1]);
        assert.deepEqual([...byTicket.dependent], [2]);
    });
});
