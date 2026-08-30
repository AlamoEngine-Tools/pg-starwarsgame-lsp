// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Where the key light stands, and how big a shadow frustum has to be to cover the model.
//
// Kept apart from the viewport so the angles can be tested without a GPU, and stated in the same
// azimuth/elevation terms the camera presets already use - a reader who has moved the camera round a
// model already knows what these two numbers mean.

/** A direction, or a position on the unit sphere. */
export interface Vector3 {
    x: number;
    y: number;
    z: number;
}

/** Where the key light sits when nothing has moved it: high, and over the viewer's right shoulder. */
export const DEFAULT_LIGHT_AZIMUTH_DEGREES = 45;

/**
 * Elevation of the default key light.
 *
 * Not straight overhead. A light directly above a model flattens every horizontal surface into one
 * value and drops the shadow into a puddle under the hull, which is the one place it tells you
 * nothing - a raking light is what makes panel lines and hardpoint edges read.
 */
export const DEFAULT_LIGHT_ELEVATION_DEGREES = 45;

/**
 * The unit vector a light at these angles points FROM, in the same frame the camera presets use:
 * Y up, azimuth measured round the Y axis from +Z, elevation up from the horizon.
 *
 * Returned as a direction rather than a position because the distance is the shadow frustum's
 * business, not the caller's - a directional light has no position that matters to its shading.
 */
export function lightDirection(azimuthDegrees: number, elevationDegrees: number): Vector3 {
    const azimuth = (azimuthDegrees * Math.PI) / 180;
    const elevation = (elevationDegrees * Math.PI) / 180;
    const horizontal = Math.cos(elevation);

    return {
        x: horizontal * Math.sin(azimuth),
        y: Math.sin(elevation),
        z: horizontal * Math.cos(azimuth),
    };
}

/**
 * The orthographic half-extent and depth range a shadow camera needs to cover a model of this size.
 *
 * A directional light's shadow camera does not follow the scene on its own, and three's default
 * frustum is five units across - which on anything bigger than an infantry model puts the whole hull
 * outside it and renders no shadow at all, while on a fighter it wastes most of the map's resolution
 * on empty space. Both failures look like "shadows do not work".
 *
 * The margin is for the shadow CASTER, which can stand well outside the frame it darkens: geometry
 * behind the model relative to the light still has to be inside the frustum to be drawn into the
 * depth map.
 */
export function shadowFrustum(radius: number): { extent: number; near: number; far: number } {
    const extent = Math.max(radius, 1) * 1.6;

    return { extent, near: 0.01 * extent, far: extent * 6 };
}
