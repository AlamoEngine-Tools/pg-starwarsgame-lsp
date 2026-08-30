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
 * starboard hardpoints.
 *
 * The rim lies on a SPHERE of radius `range`, not on a plane at that distance. That distinction is
 * the whole shape of the thing: the plane reading is `range * tan(halfAngle)`, and the Nebulon B
 * declares 175 x 160 degrees, so tan(87.5) = 22.9 turned a 1100-unit weapon into a flat sheet
 * 25000 units across. A firing arc is every DIRECTION within the half-angle, out to the range - so
 * every rim point is exactly `range` from the muzzle, and nothing runs away as the arc widens.
 *
 * `Fire_Cone_Width` opens it across Y and `Fire_Cone_Height` across Z, so a turret with a wide
 * traverse and little elevation reads as the flat fan it actually is.
 *
 * Both tags are FULL angles in degrees. The engine halves them itself - `Fire_Cone_Width / 2.0` -
 * and tests the two axes SEPARATELY, which makes the shape a rectangular pyramid rather than a
 * cone of any kind. See {@link coneDirection}.
 */
export function fireConePoints(
    widthDegrees: number, heightDegrees: number, range: number, segments: number,
): { apex: Point; rim: Point[] } {
    const halfWidth = halfYaw(widthDegrees);
    const halfHeight = halfElevation(heightDegrees);

    const rim: Point[] = [];

    for (let i = 0; i < segments; i++) {
        const around = (i / segments) * Math.PI * 2;
        const [x, y, z] = coneDirection(halfWidth, halfHeight, 1, around);

        rim.push([x * range, y * range, z * range]);
    }

    return { apex: [0, 0, 0], rim };
}

/**
 * One direction on the cone, `ring` of the way out from the axis at `around` azimuth.
 *
 * A SPHERICAL RECTANGLE - bounded by two meridians and two parallels - because that is exactly what
 * the engine tests. It transforms the target into the fire bone's frame, reduces the offset to two
 * angles, and checks each against its own half-angle independently:
 *
 * ```c
 * target_z =  atan2(y, x)                  // yaw
 * target_y = -atan2(z, sqrt(x*x + y*y))    // elevation, sign-flipped
 *
 * if (target_z > Fire_Cone_Width  / 2.0) return false;
 * if (target_y > Fire_Cone_Height / 2.0) return false;
 * ```
 *
 * **The elevation denominator is the HORIZONTAL distance, not x.** That one detail decides the
 * shape: it makes the vertical limit a parallel of latitude rather than a plane, so the region is a
 * lat-long rectangle on the sphere and the natural parameterisation is the spherical one below.
 * Reading it as `atan2(z, x)` instead builds a rectangular pyramid, which is a different solid -
 * wrong everywhere off the vertical plane, and wider in elevation than the engine allows.
 *
 * Two consequences, both visible. The tags are FULL angles: the engine halves them right there. And
 * the CORNERS reach both limits at once - a 90-by-90 arc admits a direction 60 degrees off axis
 * (`acos(cos45 * cos45)`), where a shape trading one angle against the other stops at 45.
 *
 * This shape has had two wrong ancestors, and neither mistake is this one:
 *
 * - An ELLIPSE, which traded the angles against each other. Near the diagonal of a 175-by-160 arc
 *   it put elevation past the declared limit, so it drew directions the engine rejects while
 *   refusing corners it accepts.
 * - A PRINGLE, from yawing by `halfWidth * cos` and then pitching by `halfHeight * sin`. That ADDS
 *   two angular offsets and sent the rim behind the muzzle twice per turn.
 *
 * Nothing here can wrap or run away: the parameterisation is angles on a sphere throughout, so a
 * 360-degree traverse is a full ring rather than something needing a clamp.
 */
function coneDirection(
    halfWidth: number, halfHeight: number, ring: number, around: number,
): [number, number, number] {
    const cos = Math.cos(around);
    const sin = Math.sin(around);

    // How far the rectangle's border lies at this azimuth - its polar radius. `min` is what makes
    // it a rectangle rather than a rounded shape: whichever limit is reached FIRST stops the ray,
    // which is the same "either check may reject" the engine applies.
    const toSide = Math.abs(cos) < MIN_HALF_ANGLE
        ? Infinity
        : Math.max(halfWidth, MIN_HALF_ANGLE) / Math.abs(cos);
    const toTop = Math.abs(sin) < MIN_HALF_ANGLE
        ? Infinity
        : Math.max(halfHeight, MIN_HALF_ANGLE) / Math.abs(sin);

    const radius = ring * Math.min(toSide, toTop);
    const yaw = radius * cos;
    const elevation = radius * sin;

    // Spherical coordinates about the bone's +X axis: yaw is the azimuth, elevation the latitude.
    // Unit length by construction, which is what puts every rim point exactly `range` from the
    // muzzle once the caller scales it - a firing arc is every DIRECTION within the limits out to
    // the range, and the rim belongs on a sphere rather than on a plane at that distance.
    //
    // Read it back and you get the engine's own pair: `atan2(y, x) === yaw` and
    // `atan2(z, hypot(x, y)) === elevation`.
    return [
        Math.cos(elevation) * Math.cos(yaw),
        Math.cos(elevation) * Math.sin(yaw),
        Math.sin(elevation),
    ];
}

/** Keeps a zero-width arc from dividing by zero; far below anything an author would notice. */
const MIN_HALF_ANGLE = 1e-6;

/**
 * Half the declared traverse, in radians, capped at a half-turn.
 *
 * A full turn is the real limit of an azimuth and 11 shipped hardpoints declare exactly 360, so this
 * caps at 180 a side rather than at the 89.5 the old `tan` build needed. Nothing blows up on a
 * sphere.
 */
function halfYaw(degrees: number): number {
    return Math.min((Math.max(0, degrees) / 2) * (Math.PI / 180), Math.PI);
}

/**
 * Half the declared elevation, in radians, capped just short of the pole.
 *
 * 90 degrees a side IS the whole sky above and below; going past it would fold the arc back over
 * itself. Stopping fractionally short keeps the cap a surface rather than a degenerate point, and
 * no shipped hardpoint declares more than 180 total anyway.
 */
function halfElevation(degrees: number): number {
    const limit = (179.9 * Math.PI) / 360;

    return Math.min((Math.max(0, degrees) / 2) * (Math.PI / 180), limit);
}

/**
 * How long to actually DRAW a firing cone, given the subject's size and how wide the arc is.
 *
 * The declared reach and the drawn reach are different questions once the cone is a solid. A
 * capital ship's arcs reach 2000 units on a hull about 600 long: drawn in full the camera sits
 * inside them and the viewport is one flat orange field, which tells a reader less than the wire
 * outline it replaced. What the gizmo is actually for is the DIRECTION and the SPREAD, and both
 * read at any length.
 *
 * Scaled to the subject rather than capped at a constant, for the reason `groundRange` gives: the
 * corpus runs from a two-unit trooper to a thousand-unit command centre, so no single number suits
 * it. A reach that already fits is left exactly as declared - shortening a fighter's 500 would
 * misreport a weapon whose whole reach the reader can see.
 *
 * And scaled AGAIN by the width, because a length that suits one arc drowns another. A 20-degree
 * cone is a spike and can be long; a 160-degree one is very nearly a dome, and at the same length
 * it simply encloses the camera - which is what six of the Star Destroyer's weapons did, leaving one
 * orange field with the ship lost somewhere inside it. The wide ones are pulled in until the reader
 * can stand outside and see the hull they belong to.
 *
 * The row still states the true range in units, so nothing is hidden - only drawn shorter.
 */
export function drawnConeRange(
    range: number, modelSize: number,
    widthDegrees = 0, heightDegrees = 0,
): number {
    const size = Number.isFinite(modelSize) && modelSize > 0 ? modelSize : 100;
    const limit = size * spanFactor(widthDegrees, heightDegrees);

    return range > limit ? limit : range;
}

/**
 * How many subject-lengths a cone of this width may be drawn at.
 *
 * Judged on the WIDER of the two half-angles, since that is the one that decides whether the shape
 * encloses anything. Generous below a quarter-turn - a firing arc IS long, and a reader wants to
 * see the reach - and tightening from there to something that stays clear of the hull at the
 * near-hemispherical widths capital ships declare.
 */
function spanFactor(widthDegrees: number, heightDegrees: number): number {
    const widest = Math.max(halfYaw(widthDegrees), halfElevation(heightDegrees));
    const from = Math.PI / 12;
    const to = Math.PI / 2;

    const t = Math.min(1, Math.max(0, (widest - from) / (to - from)));

    return WIDE_SPAN + (NARROW_SPAN - WIDE_SPAN) * (1 - t);
}

/** Subject-lengths a narrow cone may reach. */
const NARROW_SPAN = 2;

/** Subject-lengths a near-hemispherical one may, where the shape is a dome around the ship. */
const WIDE_SPAN = 0.5;

/**
 * The filled surface of a firing arc, as triangles.
 *
 * A CONE WITH A ROUNDED BOTTOM: straight walls from the muzzle out to the rim, closed by a bulged
 * cap that lies on the sphere of radius `range`. That is what a firing arc is - every direction
 * within the angular limits, out to the reach - and both halves matter. The walls carry a narrow
 * arc, where the cap is a small bulge on the end of a long spike; the cap carries a wide one, where
 * the walls shrink to nothing and the shape is most of a dome.
 *
 * The cap is sampled over RINGS from the axis outwards, which is what puts vertices in the interior
 * of the patch. A fan from the apex to a rim curve has none, and that is exactly why it could not
 * hold the shape at the Nebulon B's 175 by 160 degrees.
 */
export function fireConeSurface(
    widthDegrees: number, heightDegrees: number, range: number,
    segments: number, rings: number,
): number[] {
    if (range <= 0) {
        return [];
    }

    const halfWidth = halfYaw(widthDegrees);
    const halfHeight = halfElevation(heightDegrees);

    const at = (ring: number, around: number): [number, number, number] => {
        const [x, y, z] = coneDirection(halfWidth, halfHeight, ring, around);

        return [x * range, y * range, z * range];
    };

    const positions: number[] = [];
    const step = (Math.PI * 2) / segments;

    for (let r = 0; r < rings; r++) {
        const inner = r / rings;
        const outer = (r + 1) / rings;

        for (let i = 0; i < segments; i++) {
            // `i + 1` wraps to 0 at the seam, so the last column joins the first and there is no
            // slit to see through.
            const a = i * step;
            const b = ((i + 1) % segments) * step;

            const innerA = at(inner, a);
            const innerB = at(inner, b);
            const outerA = at(outer, a);
            const outerB = at(outer, b);

            positions.push(...innerA, ...outerA, ...outerB);
            positions.push(...innerA, ...outerB, ...innerB);
        }
    }

    // The walls, from the muzzle to the rim. Left off while the cap was the whole gizmo, on the
    // grounds that they were a sliver at capital-ship angles - true there, and quite wrong for the
    // 20-degree arcs that most of the corpus actually declares, where the walls ARE the cone and
    // their absence left a disc floating in space with nothing joining it to the gun.
    for (let i = 0; i < segments; i++) {
        const a = i * step;
        const b = ((i + 1) % segments) * step;

        positions.push(0, 0, 0, ...at(1, b), ...at(1, a));
    }

    return positions;
}

/**
 * The EDGES of a firing arc, as line segments.
 *
 * The filled cone alone is a soft orange haze: at 10% opacity it says roughly where the guns bear
 * and nothing about where the arc stops, and fifty of them overlapping on a capital ship blur into
 * one field. The outline is what gives each cone a readable boundary - and it is the shape the
 * gizmo was originally drawn as, before the fill replaced it wholesale rather than joining it.
 *
 * Two parts: the rim as a closed loop, which is the mouth of the cone, and a few ribs from the
 * muzzle out to it, which say where the cone comes from. Without the ribs a wide arc reads as a
 * hoop floating in front of the ship.
 *
 * Traced from {@link coneDirection}, the same function the surface uses, so the edge always sits
 * exactly on the surface it edges rather than near it.
 */
export function fireConeOutline(
    widthDegrees: number, heightDegrees: number, range: number,
    segments: number, ribs: number,
): number[] {
    if (range <= 0) {
        return [];
    }

    const halfWidth = halfYaw(widthDegrees);
    const halfHeight = halfElevation(heightDegrees);

    const at = (around: number): [number, number, number] => {
        const [x, y, z] = coneDirection(halfWidth, halfHeight, 1, around);

        return [x * range, y * range, z * range];
    };

    const positions: number[] = [];
    const step = (Math.PI * 2) / segments;

    for (let i = 0; i < segments; i++) {
        positions.push(...at(i * step), ...at(((i + 1) % segments) * step));
    }

    // Spread evenly rather than placed on the two axes. Four ribs at the quarters land on the axes
    // anyway, and the axes are the least informative places to draw one - the CORNERS are where
    // this shape says something a rounder one would not.
    for (let i = 0; i < ribs; i++) {
        positions.push(0, 0, 0, ...at((i / ribs) * Math.PI * 2));
    }

    return positions;
}
