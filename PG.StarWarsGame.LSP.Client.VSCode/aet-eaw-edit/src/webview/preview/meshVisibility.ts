// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// How a sub-mesh is switched off without taking the skeleton under it away.
//
// `Object3D.visible = false` prunes the WHOLE SUBTREE - three stops descending at the first
// invisible object. That is right for a bone, where the reader unticking a limb means the limb and
// everything on it, and wrong for a mesh, because a mesh node is very often a bone node too: the
// exporter writes one node per bone and hangs the first mesh attached to that bone on the node
// itself.
//
// `W_fuel_cell_1.alo` is the case that proved it. Its three meshes all attach to bone 0, so the
// FIRST of them - the shadow volume - became the Root node, and the shadow volume is gated off by
// default because it is not something the engine ever draws. Switching it off switched off Root,
// and with it the entire model; the hull only appeared if you ticked the shadow mesh back on.
//
// The layer mask is the mechanism that separates the two. three tests it per object and keeps
// descending either way, so clearing it hides exactly one mesh and nothing below it.
//
// `boneNodes.ts` now splits those doubled-up nodes apart at load, so a mesh generally has no
// children left to lose. This stays because it is the encoding that CANNOT go wrong: the two
// questions are different questions, and answering both with `visible` is what made three separate
// defects out of one exporter habit.

/** The layer three renders by default, and the only one this viewport uses. */
const DEFAULT_LAYER = 0;

/** The parts of an Object3D this module touches. Narrow so it can be tested without three. */
export interface Layered {
    layers: { enable(channel: number): void; disable(channel: number): void; mask: number };
}

/**
 * Shows or hides one mesh, leaving everything parented to it alone.
 *
 * Never write `visible` on a part mesh instead of calling this - see the note at the top for what
 * that costs.
 */
export function setMeshDrawn(mesh: Layered, drawn: boolean): void {
    if (drawn) {
        mesh.layers.enable(DEFAULT_LAYER);
    } else {
        mesh.layers.disable(DEFAULT_LAYER);
    }
}

/** Whether this mesh is being drawn, ignoring anything its ancestors are doing. */
export function meshDrawn(mesh: Layered): boolean {
    return (mesh.layers.mask & (1 << DEFAULT_LAYER)) !== 0;
}
