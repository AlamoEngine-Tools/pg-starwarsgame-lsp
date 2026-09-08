// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Whether a faction colour is one a player can actually use.
//
// Two units the modder can tell apart on a swatch grid are not necessarily two units a player can
// tell apart mid-battle, at a distance, on someone else's monitor - and roughly one man in twelve
// sees red and green differently. The vanilla data makes the point on its own: Bothan, Hostile and
// Sarlacc all ship 153,21,223, and Empire and Imperial both ship 54,134,242.

import type { PreviewRgba } from '../../protocol/modelPreview';

/** CIE L*a*b*. */
export interface Lab {
    l: number;
    a: number;
    b: number;
}

/** The three dichromacies worth simulating. */
export type ColourBlindness = 'protanopia' | 'deuteranopia' | 'tritanopia';

/** Something worth telling the author about a colour. */
export interface ColourFinding {
    severity: 'warning' | 'info';
    message: string;
}

/**
 * How close two colours may be before a player cannot tell the units apart.
 *
 * CIEDE2000 is calibrated so that 1.0 is roughly a just-noticeable difference on adjacent patches
 * under good light. A unit is neither adjacent nor well lit nor still, so the bar for "these read as
 * the same faction" sits far above that. 12 is a judgement call, not a standard.
 */
const COLLISION_DELTA_E = 12;

/** The dark of space, and a mid daylight terrain, as the two backdrops a unit is seen against. */
const BACKDROPS: { name: string; colour: PreviewRgba }[] = [
    { name: 'the space backdrop', colour: { r: 10, g: 12, b: 20, a: 255 } },
    { name: 'daylight terrain', colour: { r: 138, g: 132, b: 112, a: 255 } },
];

/**
 * Contrast below which a unit stops reading against its background.
 *
 * WCAG's 3:1 for large graphics is the nearest published anchor. A capital ship is large, so 3:1 is
 * the floor rather than a target.
 */
const MIN_CONTRAST = 3;

/**
 * Perceptual distance below which a unit also fails to separate from its background.
 *
 * Required IN ADDITION to the contrast test, because WCAG contrast is a text-legibility measure and
 * only looks at luminance. An amber unit on daylight terrain has almost the same luminance as the
 * ground and is still impossible to miss, because the hue differs enormously - flagging that would
 * be noise, and a checker people learn to ignore is worse than none. Something has to be close in
 * BOTH luminance and hue before it genuinely disappears.
 */
const BACKDROP_DELTA_E = 20;

function channelToLinear(value: number): number {
    const scaled = Math.min(255, Math.max(0, value)) / 255;
    return scaled <= 0.04045 ? scaled / 12.92 : ((scaled + 0.055) / 1.055) ** 2.4;
}

/** sRGB to CIE L*a*b*, through XYZ under the D65 white point. */
export function rgbToLab(colour: PreviewRgba): Lab {
    const r = channelToLinear(colour.r);
    const g = channelToLinear(colour.g);
    const b = channelToLinear(colour.b);

    const x = (r * 0.4124564 + g * 0.3575761 + b * 0.1804375) / 0.95047;
    const y = r * 0.2126729 + g * 0.7151522 + b * 0.072175;
    const z = (r * 0.0193339 + g * 0.119192 + b * 0.9503041) / 1.08883;

    const f = (t: number): number =>
        t > 0.008856 ? Math.cbrt(t) : 7.787 * t + 16 / 116;

    return { l: 116 * f(y) - 16, a: 500 * (f(x) - f(y)), b: 200 * (f(y) - f(z)) };
}

const toRadians = (degrees: number): number => (degrees * Math.PI) / 180;
const toDegrees = (radians: number): number => (radians * 180) / Math.PI;

/**
 * CIEDE2000 colour difference.
 *
 * Implemented from Sharma, Wu and Dalal's formulation, and checked against their published test
 * pairs - the hue-difference wrapping and the rotation term are both easy to get wrong in ways that
 * still return plausible numbers.
 */
export function deltaE2000(first: Lab, second: Lab): number {
    const avgL = (first.l + second.l) / 2;

    const c1 = Math.hypot(first.a, first.b);
    const c2 = Math.hypot(second.a, second.b);
    const avgC = (c1 + c2) / 2;

    const g = 0.5 * (1 - Math.sqrt(avgC ** 7 / (avgC ** 7 + 25 ** 7)));

    const a1 = first.a * (1 + g);
    const a2 = second.a * (1 + g);

    const cp1 = Math.hypot(a1, first.b);
    const cp2 = Math.hypot(a2, second.b);
    const avgCp = (cp1 + cp2) / 2;

    const hue = (a: number, b: number): number => {
        if (a === 0 && b === 0) {
            return 0;
        }
        const angle = toDegrees(Math.atan2(b, a));
        return angle >= 0 ? angle : angle + 360;
    };

    const h1 = hue(a1, first.b);
    const h2 = hue(a2, second.b);

    const deltaL = second.l - first.l;
    const deltaC = cp2 - cp1;

    let deltah = 0;
    if (cp1 * cp2 !== 0) {
        deltah = h2 - h1;
        if (deltah > 180) {
            deltah -= 360;
        } else if (deltah < -180) {
            deltah += 360;
        }
    }

    const deltaH = 2 * Math.sqrt(cp1 * cp2) * Math.sin(toRadians(deltah) / 2);

    let avgH = h1 + h2;
    if (cp1 * cp2 !== 0) {
        if (Math.abs(h1 - h2) > 180) {
            avgH += h1 + h2 < 360 ? 360 : -360;
        }
        avgH /= 2;
    }

    const t = 1
        - 0.17 * Math.cos(toRadians(avgH - 30))
        + 0.24 * Math.cos(toRadians(2 * avgH))
        + 0.32 * Math.cos(toRadians(3 * avgH + 6))
        - 0.2 * Math.cos(toRadians(4 * avgH - 63));

    const sl = 1 + (0.015 * (avgL - 50) ** 2) / Math.sqrt(20 + (avgL - 50) ** 2);
    const sc = 1 + 0.045 * avgCp;
    const sh = 1 + 0.015 * avgCp * t;

    const rt = -2
        * Math.sqrt(avgCp ** 7 / (avgCp ** 7 + 25 ** 7))
        * Math.sin(toRadians(60 * Math.exp(-(((avgH - 275) / 25) ** 2))));

    return Math.sqrt(
        (deltaL / sl) ** 2
        + (deltaC / sc) ** 2
        + (deltaH / sh) ** 2
        + rt * (deltaC / sc) * (deltaH / sh),
    );
}

/** WCAG relative luminance. */
function relativeLuminance(colour: PreviewRgba): number {
    return 0.2126 * channelToLinear(colour.r)
        + 0.7152 * channelToLinear(colour.g)
        + 0.0722 * channelToLinear(colour.b);
}

/** WCAG contrast ratio, between 1 and 21. Order does not matter. */
export function contrastRatio(first: PreviewRgba, second: PreviewRgba): number {
    const a = relativeLuminance(first);
    const b = relativeLuminance(second);

    return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
}

/**
 * Dichromat simulation matrices, applied in linear RGB.
 *
 * Viénot, Brettel and Mollon's 1999 approach, in the widely used linear-RGB form. It is an
 * approximation of a dichromat's experience, not a claim about anyone's vision - what it is good for
 * is telling two colours apart, which is exactly the question here.
 */
const SIMULATION: Record<ColourBlindness, number[][]> = {
    protanopia: [
        [0.1121, 0.8853, -0.0005],
        [0.1127, 0.8897, -0.0001],
        [0.0045, 0.0, 1.0019],
    ],
    deuteranopia: [
        [0.292, 0.7054, -0.0003],
        [0.2934, 0.7089, 0.0],
        [-0.0195, 0.0333, 0.9912],
    ],
    tritanopia: [
        [1.0163, 0.1001, -0.1164],
        [0.0088, 0.7906, 0.2007],
        [0.0091, 0.7137, 0.2775],
    ],
};

function linearToChannel(value: number): number {
    const clamped = Math.min(1, Math.max(0, value));
    const encoded = clamped <= 0.0031308
        ? clamped * 12.92
        : 1.055 * clamped ** (1 / 2.4) - 0.055;

    return Math.round(encoded * 255);
}

/** How a colour reads to someone with one of the three dichromacies. */
export function simulateColourBlindness(
    colour: PreviewRgba, kind: ColourBlindness,
): PreviewRgba {
    const [row0, row1, row2] = SIMULATION[kind];

    const r = channelToLinear(colour.r);
    const g = channelToLinear(colour.g);
    const b = channelToLinear(colour.b);

    return {
        r: linearToChannel(row0[0] * r + row0[1] * g + row0[2] * b),
        g: linearToChannel(row1[0] * r + row1[1] * g + row1[2] * b),
        b: linearToChannel(row2[0] * r + row2[1] * g + row2[2] * b),
        a: 255,
    };
}

/**
 * A faction to compare against.
 *
 * Optional as well as nullable, so a `PreviewFaction` straight off the wire satisfies it - the
 * protocol marks absent colours both ways.
 */
export interface NamedColour {
    name: string;
    color?: PreviewRgba | null;
}

/**
 * Everything worth saying about one faction colour.
 *
 * Findings are sentences on purpose. "Indistinguishable from Empire under deuteranopia" is something
 * an author can act on; "dE 3.4" is a number they have to go and look up first.
 */
export function reviewFactionColour(
    colour: PreviewRgba, name: string, others: readonly NamedColour[],
): ColourFinding[] {
    const findings: ColourFinding[] = [];
    const lab = rgbToLab(colour);

    for (const other of others) {
        const against = other.color ?? null;
        if (against === null || other.name === name) {
            continue;
        }

        const distance = deltaE2000(lab, rgbToLab(against));
        if (distance < COLLISION_DELTA_E) {
            findings.push({
                severity: 'warning',
                message: distance < 1
                    ? `The same colour as ${other.name}.`
                    : `Hard to tell apart from ${other.name}.`,
            });
            continue;
        }

        // Only worth mentioning when normal vision separates them but a dichromat's does not.
        for (const kind of ['protanopia', 'deuteranopia', 'tritanopia'] as const) {
            const simulated = deltaE2000(
                rgbToLab(simulateColourBlindness(colour, kind)),
                rgbToLab(simulateColourBlindness(against, kind)));

            if (simulated < COLLISION_DELTA_E) {
                findings.push({
                    severity: 'warning',
                    message: `Indistinguishable from ${other.name} under ${kind}.`,
                });
                break;
            }
        }
    }

    for (const backdrop of BACKDROPS) {
        const lowContrast = contrastRatio(colour, backdrop.colour) < MIN_CONTRAST;
        const closeInHue = deltaE2000(lab, rgbToLab(backdrop.colour)) < BACKDROP_DELTA_E;

        if (lowContrast && closeInHue) {
            findings.push({
                severity: 'info',
                message: `Hard to pick out against ${backdrop.name}.`,
            });
        }
    }

    return findings;
}
