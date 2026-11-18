// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The viewport's destruction: each piece of wreckage a destroyed hardpoint throws, flown and expired, and
// the spin-away a dying ship runs before it is gone, with the pose it is restored to on repair. It reads
// the parts and adds and removes the wreckage through the assembly; nothing else.

import {
    type PreviewSpinAway,
    type PreviewBreakoffProp,
} from "../../protocol/modelPreview";
import * as THREE from "three";
import {
    type BreakoffAnchor,
    breakoffLifetime,
    breakoffPose,
} from "./breakoff";
import { BREAKOFF_ATTACHMENT } from "./loadedPart";
import { disposeTree } from "./partAssembly";
import { spinAwayPose } from "./spinAway";
import { type ViewportContext } from "./viewportContext";

/** One piece of wreckage in the air. */
interface BreakoffInstance {
    key: string;
    /** The loaded part carrying its geometry - a passive subject of its own. */
    partId: string;
    root: THREE.Object3D;
    prop: PreviewBreakoffProp;
    /** Where the hardpoint was when it broke off. The debris does not follow the ship. */
    origin: THREE.Vector3;
    facing: THREE.Quaternion;
    age: number;
    lifetime: number;
    /**
     * Whether its time is up and its geometry has gone.
     *
     * The empty root stays in the scene: `Death_Explosions` fires where the piece FINISHED, not
     * where it broke off, and an empty node is the only thing that still knows where that was.
     */
    expired: boolean;
}

/** What DestructionRig still asks of the viewport itself: what only the viewport has. */
export interface DestructionHost {
    readonly modelScale: number;
    readonly onBreakoffExpired:
        ((key: string, prop: PreviewBreakoffProp) => void) | null;
}

export class DestructionRig {
    /** Wreckage currently in the air, each on its own clock. */
    private readonly breakoffs: BreakoffInstance[] = [];
    /**
     * The unit flying off and coming apart, for one that declares no death clone.
     *
     * Driven HERE rather than from the panel because it is a per-frame thing and this class already
     * owns the frame. The panel says start and is told when it is over.
     */
    private spinAway: {
        partId: string;
        spin: PreviewSpinAway;
        elapsed: number;
        onDone: () => void;
    } | null = null;
    /**
     * Where the flying part sat before it was pushed, so putting it back is exact.
     *
     * Held SEPARATELY from `spinAway` and outlives it. The spin ends the moment the wreck explodes,
     * but the part is still sitting out there where it finished - and clearing both together left
     * `clearSpinAway` with nothing to restore, so Repair brought the ship back visible at the far
     * end of its own death run, 270 units off the grid.
     */
    private spinRestore: {
        partId: string;
        position: THREE.Vector3;
        quaternion: THREE.Quaternion;
    } | null = null;

    constructor(
        private readonly context: ViewportContext,
        private readonly host: DestructionHost,
    ) {}

    /**
     * Drops a piece of wreckage where a hardpoint used to be.
     *
     * A PASSIVE SUBJECT loaded by the one loader, which is all that is left here that is specific
     * to debris: where the hardpoint was, and the clock it runs on. It used to load itself, and the
     * hand copy drifted - four of its six duplicated steps were added one bug report at a time,
     * and nine more were simply missing.
     *
     * Its own subject rather than another piece of the ship: debris is transient, it is removed on
     * its own clock, it is drawn by its own file's rules, and re-parenting the hardpoint's own model
     * would fight the destruction rules that hid it.
     */
    async addBreakoff(
        key: string,
        glbBase64: string,
        at: BreakoffAnchor,
        prop: PreviewBreakoffProp,
    ): Promise<void> {
        // Where the hardpoint was, in world space, captured once - the debris does not follow the ship.
        // WHICH node that is, is `breakoffAnchor`'s decision and not this one's: a hardpoint model can
        // carry a copy of the hull's own bones, and resolving the attachment bone by name inside it
        // applied the offset twice.
        const anchor = this.context.assembly.attachmentFor(
            at.partId,
            at.bone,
            at.boneIndex,
        );
        const origin = anchor.getWorldPosition(new THREE.Vector3());
        const facing = anchor.getWorldQuaternion(new THREE.Quaternion());

        // The wreck's part id and its subject id are the same string on purpose: it is one model
        // and one subject, and the effects it trails already address it by this name.
        const wreckId = `${BREAKOFF_ATTACHMENT}${key}`;

        const root = await this.context.assembly.addPart(
            wreckId,
            glbBase64,
            undefined,
            undefined,
            {
                subjectId: wreckId,
                pose: { position: origin, quaternion: facing },
            },
        );

        this.breakoffs.push({
            key,
            partId: wreckId,
            root,
            prop,
            origin,
            facing,
            age: 0,
            lifetime: breakoffLifetime(prop),
            expired: false,
        });
    }

    /**
     * Clears the wreckage ONE hardpoint shed.
     *
     * Repairing a single hardpoint from its own card has to take its debris with it, exactly as
     * repairing the whole target does - otherwise the hardpoint is back on the hull and its wreck
     * is still tumbling away beside it.
     */
    clearBreakoff(key: string): void {
        // Spliced in place rather than reassigned: the array is readonly on the field, and the
        // wholesale clear beside this one empties it the same way for the same reason.
        for (let at = this.breakoffs.length - 1; at >= 0; at--) {
            if (this.breakoffs[at].key !== key) {
                continue;
            }

            this.context.assembly.removePart(this.breakoffs[at].partId);
            this.breakoffs.splice(at, 1);
        }
    }

    /** Clears every piece of wreckage - a repair puts the hardpoints back, so the debris goes. */
    clearBreakoffs(): void {
        for (const debris of this.breakoffs) {
            this.context.assembly.removePart(debris.partId);
        }

        this.breakoffs.length = 0;
    }

    /** Whether a piece of wreckage for this key is already in the air. */
    hasBreakoff(key: string): boolean {
        return this.breakoffs.some((debris) => debris.key === key);
    }

    /** Starts the wreck on its way. `onDone` fires once, at the end of the declared time. */
    startSpinAway(
        partId: string,
        spin: PreviewSpinAway,
        onDone: () => void,
    ): void {
        const part = this.context.parts.get(partId);
        if (part === undefined) {
            // Nothing to fly. Report it over at once rather than leaving the caller waiting on a
            // callback that can never come.
            onDone();
            return;
        }

        this.clearSpinAway();

        this.spinRestore = {
            partId,
            position: part.root.position.clone(),
            quaternion: part.root.quaternion.clone(),
        };

        this.spinAway = { partId, spin, elapsed: 0, onDone };
    }

    /** Puts the wreck back where it started, whether it is still flying or already exploded. */
    clearSpinAway(): void {
        this.spinAway = null;

        const home = this.spinRestore;
        this.spinRestore = null;

        if (home === null) {
            return;
        }

        const part = this.context.parts.get(home.partId);
        if (part !== undefined) {
            part.root.position.copy(home.position);
            part.root.quaternion.copy(home.quaternion);
        }
    }

    // ── particles ──────────────────────────────────────────────────────

    /**
     * Drifts the wreckage and retires it when its time is up.
     *
     * Recomputed from the age rather than integrated per frame: an incremental drift accumulates
     * float error, and debris that ends up somewhere slightly different each run is exactly what
     * makes two looks at the same hardpoint disagree.
     */
    advanceBreakoffs(deltaSeconds: number): void {
        if (this.breakoffs.length === 0) {
            return;
        }

        const alive: BreakoffInstance[] = [];

        for (const debris of this.breakoffs) {
            // An expired piece is a marker and nothing more: it holds the place its explosion goes
            // off, and it must not keep drifting away from it.
            if (debris.expired) {
                alive.push(debris);
                continue;
            }

            debris.age += deltaSeconds;

            if (debris.age >= debris.lifetime) {
                // The GEOMETRY goes; the root stays. `Death_Explosions` fires where the piece
                // finished, and once the root is gone nothing in the scene knows where that was -
                // which is why the wreck simply vanished with no blast at all.
                const subjectId =
                    this.context.parts.get(debris.partId)?.subjectId ??
                    debris.partId;

                for (const child of [...debris.root.children]) {
                    this.context.assembly.dropRegistrations(subjectId, child);
                    child.removeFromParent();
                    disposeTree(child);
                }

                debris.expired = true;
                alive.push(debris);
                this.host.onBreakoffExpired?.(debris.key, debris.prop);
                continue;
            }

            const pose = breakoffPose(debris.prop, debris.age);

            debris.root.position.set(
                debris.origin.x + pose.offset.x,
                debris.origin.y + pose.offset.y,
                debris.origin.z + pose.offset.z,
            );

            const spin = new THREE.Euler(
                pose.rotation.x * (Math.PI / 180),
                pose.rotation.y * (Math.PI / 180),
                pose.rotation.z * (Math.PI / 180),
            );

            debris.root.quaternion
                .copy(debris.facing)
                .multiply(new THREE.Quaternion().setFromEuler(spin));

            alive.push(debris);
        }

        this.breakoffs.length = 0;
        this.breakoffs.push(...alive);
    }

    advanceSpinAway(dt: number): void {
        const flying = this.spinAway;
        if (flying === null) {
            return;
        }

        flying.elapsed += dt;

        const part = this.context.parts.get(flying.partId);
        const home = this.spinRestore;
        const pose = spinAwayPose(flying.spin, flying.elapsed);

        if (part !== undefined && home !== null) {
            // Offsets are in the model's own space, and the exporter's Z-up to Y-up rotation lives
            // on a node INSIDE the part - so adding them to the part root is adding them in the
            // space the numbers were computed in.
            //
            // Divided by the geometry scale, because the travel term is
            // `Max_Speed * frames` - an XML speed, and therefore a WORLD distance - while the part
            // root sits inside the scaled node. Without it the wreck and the explosion that marks
            // where it stopped part company on any subject that declares a scale: `spinAwayEnd`
            // hands that same offset to a particle anchored on the UNSCALED model root.
            const travel = 1 / this.host.modelScale;

            part.root.position.set(
                home.position.x + pose.offset.x * travel,
                home.position.y + pose.offset.y * travel,
                home.position.z + pose.offset.z * travel,
            );

            // Roll about the TRAVEL axis, which is Z - the blue one. Rolling about X while flying
            // along Z is a tumble end over end, not a corkscrew.
            part.root.quaternion
                .copy(home.quaternion)
                .multiply(
                    new THREE.Quaternion().setFromAxisAngle(
                        new THREE.Vector3(0, 0, 1),
                        pose.roll,
                    ),
                );
        }

        if (pose.done) {
            // Cleared BEFORE the callback: the panel hides the part and fires the explosion from
            // in there, and a re-entrant start would otherwise be undone by this same tick.
            const done = flying.onDone;
            this.spinAway = null;
            done();
        }
    }
}
