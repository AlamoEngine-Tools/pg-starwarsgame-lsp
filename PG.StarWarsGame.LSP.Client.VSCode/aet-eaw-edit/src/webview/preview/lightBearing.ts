// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Where a light stands, in words.
//
// Two numbers on two sliders do not say where a light is. Azimuth 0 to 360 gives no clue which way
// zero faces, and a height slider running -90 to 90 marks the horizon nowhere - so the first sign
// the light has dropped under the ground plane is the shadow quietly disappearing. Both are stated
// here in the terms the reader already has from the stage's own Front / Side / Top presets.

/** Where a light stands, said in words rather than in degrees. */
export interface LightBearing {
    /** Which side it is on, named against the Front view. */
    from: string;

    /** How high it is, in a word. */
    height: string;

    /** Whether it has dropped below the ground plane. */
    belowGround: boolean;
}

/**
 * The eight points, starting at the front and turning clockwise seen from above.
 *
 * The same direction the azimuth slider moves, and the same zero the camera's Front preset uses -
 * a light at 0 stands where the reader stands when they press Front.
 */
const POINTS = [
    'the front', 'the front right', 'the right', 'the back right',
    'behind', 'the back left', 'the left', 'the front left',
];

/** Height bands, from the horizon up. Anything below it is under the floor. */
function heightWord(elevation: number): string {
    if (elevation < 0) {
        return 'below';
    }

    if (elevation === 0) {
        return 'level';
    }

    if (elevation < 25) {
        // The band that makes panel lines and hardpoint edges read, and the one worth naming.
        return 'raking';
    }

    return elevation < 65 ? 'above' : 'overhead';
}

/**
 * Reads a light's two angles back as a bearing.
 *
 * Rounds to the nearest of eight points rather than reporting the angle again: the slider steps in
 * fives, and a readout that changed wording on every step would be noise rather than orientation.
 */
export function lightBearing(azimuthDegrees: number, elevationDegrees: number): LightBearing {
    const wrapped = ((azimuthDegrees % 360) + 360) % 360;
    const point = Math.round(wrapped / 45) % POINTS.length;

    return {
        from: POINTS[point],
        height: heightWord(elevationDegrees),
        belowGround: elevationDegrees < 0,
    };
}
