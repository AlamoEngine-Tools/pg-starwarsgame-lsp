// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The drag handle's geometry: an arc a turret may actually travel, and the knob sitting on it.
//
// Separate from `fireArc.ts` because it draws a different thing. An arc there is where a weapon may
// SHOOT, drawn out to its range; a track here is where a bone may TURN, drawn at a fixed radius
// near the joint. Sharing a module would invite sharing a radius, and they are not the same
// measurement.

import type { Point } from './fireArc';
import type { TurretAxisRange } from './turretHandles';

/**
 * The plane a track is drawn in, named by the axis it turns about.
 *
 * Both are the bone's OWN axes, the same convention the sweep uses: Alamo is Z-up and the
 * exporter's Z-up-to-Y-up rotation sits on the root node, so a bone still carries the file's axes -
 * X forward, Y lateral, Z up. Traverse turns about Z; elevation turns about Y.
 */
export type TrackPlane = 'traverse' | 'elevation';

/** Which plane an axis is drawn in. There are only the two, and no roll. */
export function trackPlane(range: TurretAxisRange): TrackPlane {
    return range.axis === 'yaw' ? 'traverse' : 'elevation';
}

/**
 * A point on a track, at `degrees` off the bone's forward axis.
 *
 * Forward is local +X, so zero degrees is straight ahead - the same zero the aim readout uses and
 * the same one the fire bones point along.
 */
export function trackPoint(plane: TrackPlane, degrees: number, radius: number): Point {
    const angle = degrees * (Math.PI / 180);
    const along = Math.cos(angle) * radius;
    const across = Math.sin(angle) * radius;

    // Traverse swings X towards Y, which is lateral. Elevation swings X towards +Z, which is up -
    // hence the sign, matching the viewport's nose-up convention where a positive pitch raises the
    // barrel.
    return plane === 'traverse' ? [along, across, 0] : [along, 0, across];
}

/**
 * The track itself, as a line strip.
 *
 * It spans the LEGAL RANGE and nothing more. A full circle on a turret bounded to plus or minus 45
 * would be a lie about the unit, and the stop has to be visible BEFORE it is reached rather than
 * discovered by pushing against it.
 */
export function trackPoints(
    range: TurretAxisRange, radius: number, segments: number,
): Point[] {
    const plane = trackPlane(range);
    const span = range.free ? 360 : range.limitDegrees * 2;
    const start = range.free ? 0 : -range.limitDegrees;

    // A full ring closes on itself, so its last point is its first and drawing it twice leaves a
    // visible seam on a translucent material.
    const count = range.free ? segments : segments + 1;
    const points: Point[] = [];

    for (let i = 0; i < count; i++) {
        points.push(trackPoint(plane, start + (i / segments) * span, radius));
    }

    return points;
}

/** A line-segment buffer for the track, closed when the axis is free. */
export function trackSegments(
    range: TurretAxisRange, radius: number, segments: number,
): number[] {
    const points = trackPoints(range, radius, segments);
    const positions: number[] = [];

    for (let i = 0; i + 1 < points.length; i++) {
        positions.push(...points[i], ...points[i + 1]);
    }

    if (range.free && points.length > 1) {
        positions.push(...points[points.length - 1], ...points[0]);
    }

    return positions;
}

/**
 * The two end stops, as short ticks across the track.
 *
 * Drawn rather than left to the track simply ending: a track that stops looks the same as a track
 * that was clipped, and the reader needs to see that the end is the engine's and not the drawing's.
 * A free axis has none, which is itself the thing worth seeing.
 */
export function stopTicks(
    range: TurretAxisRange, radius: number, tickLength: number,
): number[] {
    if (range.free) {
        return [];
    }

    const plane = trackPlane(range);
    const positions: number[] = [];

    for (const degrees of [-range.limitDegrees, range.limitDegrees]) {
        positions.push(
            ...trackPoint(plane, degrees, radius - tickLength / 2),
            ...trackPoint(plane, degrees, radius + tickLength / 2));
    }

    return positions;
}

/** Where the knob sits: on the track, at the angle the turret is currently pointed. */
export function knobPoint(
    range: TurretAxisRange, degrees: number, radius: number,
): Point {
    return trackPoint(trackPlane(range), degrees, radius);
}

/**
 * How big a handle should be on this subject.
 *
 * A fraction of the model rather than a constant: the same gizmo has to be grabbable on a 40-unit
 * speeder and not swallow a 600-unit hull. Clamped at the bottom so a very small subject still
 * leaves a knob big enough to hit.
 *
 * The fraction is MEASURED rather than chosen. At 0.18 the Gargantuan's six turret tracks each
 * reached about a third of the hull's length and overlapped one another into a cyan thicket - and
 * six is a modest count, where a Star Destroyer carries dozens. At 0.06 each track sits around the
 * turret it belongs to, which is what makes twenty of them separable.
 */
export function trackRadius(modelSpan: number): number {
    return Math.max(modelSpan * TRACK_SPAN_FRACTION, MIN_TRACK_RADIUS);
}

const TRACK_SPAN_FRACTION = 0.06;
const MIN_TRACK_RADIUS = 6;
