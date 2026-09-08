// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Camera framing. Kept apart from the viewport so the arithmetic is testable without a canvas -
// "the model is off screen" is otherwise only ever found by looking.

export interface Vec3 {
    x: number;
    y: number;
    z: number;
}

export interface BoundingSphere {
    center: Vec3;
    radius: number;
}

export interface CameraPose {
    position: Vec3;
    target: Vec3;
}

/**
 * How the camera is placed by default.
 *
 * A three-quarter view, matching AloViewer's headless capture: it shows two sides and the top, so a
 * hull reads as a solid rather than a silhouette. Elevation is shallow because most of the corpus is
 * long and flat, and looking steeply down at a Star Destroyer mostly shows deck.
 */
export const DEFAULT_AZIMUTH_DEGREES = 45;
export const DEFAULT_ELEVATION_DEGREES = 20;

/**
 * Fraction of the frame the model is allowed to fill.
 *
 * Below 1 so the silhouette does not touch the edges, which reads as clipped even when it is not.
 */
const FILL_FRACTION = 0.85;

/**
 * The smallest sphere radius worth framing.
 *
 * Some shipped models are a single bone with no geometry, giving a zero-radius sphere; dividing by
 * it puts the camera at the origin, inside nothing, showing black.
 */
const MIN_RADIUS = 0.001;

/**
 * Distance at which a sphere of <paramref name="radius" /> fills {@link FILL_FRACTION} of the view.
 *
 * Uses the vertical field of view, and the horizontal one when the viewport is narrower than it is
 * tall - otherwise a portrait-shaped panel crops the sides off a wide model.
 */
export function fitDistance(radius: number, verticalFovDegrees: number, aspect: number): number {
    const safeRadius = Math.max(radius, MIN_RADIUS);
    const vertical = (verticalFovDegrees * Math.PI) / 180;

    // Horizontal FOV follows from the vertical one and the aspect ratio.
    const horizontal = 2 * Math.atan(Math.tan(vertical / 2) * Math.max(aspect, MIN_RADIUS));
    const limiting = Math.min(vertical, horizontal);

    return safeRadius / (Math.sin(limiting / 2) * FILL_FRACTION);
}

/**
 * Where to put the camera so the whole model is visible.
 *
 * Framing from the bounding sphere rather than tuning per model is what lets anything from a fighter
 * to a command centre open correctly with no per-file setup.
 */
export function frameSphere(
    sphere: BoundingSphere,
    verticalFovDegrees: number,
    aspect: number,
    azimuthDegrees: number = DEFAULT_AZIMUTH_DEGREES,
    elevationDegrees: number = DEFAULT_ELEVATION_DEGREES,
): CameraPose {
    const distance = fitDistance(sphere.radius, verticalFovDegrees, aspect);

    const azimuth = (azimuthDegrees * Math.PI) / 180;
    const elevation = (elevationDegrees * Math.PI) / 180;

    // Y is up: the exporter already rotated the model out of Alamo's Z-up into glTF's Y-up.
    const horizontal = distance * Math.cos(elevation);

    return {
        position: {
            x: sphere.center.x + horizontal * Math.sin(azimuth),
            y: sphere.center.y + distance * Math.sin(elevation),
            z: sphere.center.z + horizontal * Math.cos(azimuth),
        },
        target: { ...sphere.center },
    };
}

/**
 * Near and far planes for a model of this size, with this much going on around it.
 *
 * Derived rather than fixed. The corpus spans several orders of magnitude - an infantry model is a
 * couple of units across and a command centre is thousands - and one fixed near plane either clips
 * the small models away or z-fights the large ones into stripes.
 *
 * `reach` is how far the EFFECTS get, which is not the same question as how big the model is: Boba
 * Fett is seven units tall and his flamethrower throws particles two hundred units, so a far plane
 * fitted to the geometry alone cut the flame off halfway - and a clipped effect reads as an effect
 * that stops, not as a camera that cannot see it.
 *
 * `multiplier` is the reader's own override on top, for the trails no fit can anticipate and for
 * pulling the plane back in when a huge hull z-fights.
 */
export function clipPlanes(
    sphere: BoundingSphere, distance: number, reach = 0, multiplier = 1,
): { near: number; far: number } {
    const radius = Math.max(sphere.radius, MIN_RADIUS, reach);

    return {
        // A HUNDREDTH of the radius, not a thousandth. The depth buffer's precision is decided
        // almost entirely by the near plane - the far plane barely enters into it - and a
        // thousandth put the far/near ratio at 15000, which on a Star Destroyer left a depth step
        // of about three hundredths of a world unit at the hull. Alamo's light meshes are laid
        // straight onto the hull with `ZWriteEnable = FALSE`, `ZFunc = LESSEQUAL` and no depth bias
        // anywhere, so the engine has nothing but precision holding them apart either: at a
        // thousandth, 42% of a Victory Star Destroyer's `Lighting` mesh lost the depth test and
        // broke into speckle. The cost is that the camera clips a surface a hundredth of the
        // model's radius away instead of a thousandth, which is closer than anyone needs to get.
        near: Math.max(Math.max(sphere.radius, MIN_RADIUS) / 100, 0.01),
        far: (distance + radius) * 4 * multiplier,
    };
}

/** The sphere enclosing an axis-aligned box. */
export function sphereFromBounds(min: Vec3, max: Vec3): BoundingSphere {
    const center = {
        x: (min.x + max.x) / 2,
        y: (min.y + max.y) / 2,
        z: (min.z + max.z) / 2,
    };

    const radius = Math.sqrt(
        (max.x - center.x) ** 2 + (max.y - center.y) ** 2 + (max.z - center.z) ** 2,
    );

    return { center, radius };
}
