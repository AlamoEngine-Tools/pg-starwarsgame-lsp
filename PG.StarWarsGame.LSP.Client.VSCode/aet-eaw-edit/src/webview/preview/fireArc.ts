// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The firing-arc gizmo's geometry, in a fire bone's own space.

/** A point in the bone's local space. */
export type Point = [number, number, number];

/**
 * The apex and rim of one firing cone.
 *
 * Built along local **+X**, which is where an Alamo fire bone points. That is measured rather than
 * assumed: across every `FP_` bone of `Ev_stardestroyer.alo`, the bone's local X axis has a positive
 * dot with the direction away from the hull (0.30 to 0.93) while Z is ~0.01 - guns that fire
 * horizontally, forward. Y is the lateral axis, which is why its sign flips between the port and
 * starboard mounts.
 *
 * The cone is elliptical: `Fire_Cone_Width` opens it across Y and `Fire_Cone_Height` across Z, so a
 * turret with a wide traverse and little elevation reads as the flat fan it actually is.
 */
export function fireConePoints(
    widthDegrees: number, heightDegrees: number, range: number, segments: number,
): { apex: Point; rim: Point[] } {
    const halfWidth = halfAngle(widthDegrees);
    const halfHeight = halfAngle(heightDegrees);

    const acrossY = range * Math.tan(halfWidth);
    const acrossZ = range * Math.tan(halfHeight);

    const rim: Point[] = [];
    for (let i = 0; i < segments; i++) {
        const angle = (i / segments) * Math.PI * 2;
        rim.push([range, Math.cos(angle) * acrossY, Math.sin(angle) * acrossZ]);
    }

    return { apex: [0, 0, 0], rim };
}

/**
 * Half of a cone angle, in radians, kept short of a right angle.
 *
 * At exactly 180 degrees the half-angle is 90 and its tangent is infinite; past it the tangent goes
 * negative and the cone turns inside out, pointing behind the bone. Neither is a shape the engine
 * ever draws, so the clamp keeps the gizmo honest at the extremes.
 */
function halfAngle(degrees: number): number {
    const half = (Math.max(0, degrees) / 2) * (Math.PI / 180);
    const limit = (89 * Math.PI) / 180;

    return Math.min(half, limit);
}
