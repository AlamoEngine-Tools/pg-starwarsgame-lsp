// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Manual turret control: two axes, their legal stops, and where the reader has pointed a turret.
//
// The instrument is Blender's, reduced to what this engine actually has. Two axes and not three,
// because a turret has no roll: `Calculate_Desired_Turret_Angle` builds its answer as
// `Vector3(0.0, pitch, yaw)` and the X component is a literal zero.
//
// Everything here is the CLAMP, not the arc. They are different envelopes and that is the whole
// point of the feature - see `turretEnvelopes` at the bottom.

import type { PreviewTurret } from '../../protocol/modelPreview';

import { turretSweeps, type TurretSweep } from './deathClone';

/** The two axes a turret has. There is no third. */
export type TurretAxis = 'yaw' | 'pitch';

/**
 * The extent at or above which the engine applies no stop at all.
 *
 * `HardPointClass::Calculate_Desired_Turret_Angle` gates BOTH clamps on
 * `extent < 180.0 && extent > 0.0`, so the band that actually clamps is open at both ends.
 */
const CLAMP_BAND_MAX = 180;

/** How far a free axis is drawn: a full ring is plus or minus half a turn. */
const HALF_TURN = 180;

/** One axis of one turret: where its stops are, or that it has none. */
export interface TurretAxisRange {
    axis: TurretAxis;
    /**
     * The engine applies no clamp on this axis, so the handle is a full ring.
     *
     * True BOTH above the band and below it. See {@link axisRange} - the second case is the
     * surprising one.
     */
    free: boolean;
    /** The stop either side of rest, in degrees. 180 on a free axis. */
    limitDegrees: number;
}

/**
 * Whether an extent clamps, and to what.
 *
 * MEASURED, and transcribed rather than interpreted. The engine's gate is
 * `extent < 180.0 && extent > 0.0` on each axis independently, so:
 *
 * - `0 < extent < 180` - a real pair of stops at plus and minus the extent;
 * - `extent >= 180` - no clamp, because plus or minus 180 is already the whole circle;
 * - `extent <= 0` - ALSO no clamp. The gate excludes it, so the desired angle passes through
 *   untouched and the bone turns freely.
 *
 * That last case disagrees with the FIRING gate, where `Can_Weapon_Point_At` refuses any bearing
 * with `extent < |yaw|` and an extent of zero therefore refuses everything off dead centre. A
 * turret authored at zero would turn freely and be unable to shoot anywhere it turned. Nothing in
 * `eaw/` or `foc/` authors one - all 7 turret hardpoints declare an extent - and the preview does
 * not currently reach this case either, because `turretSweeps` drops a turret whose extents are
 * both zero. It is written down here rather than smoothed over: the two paths really do disagree.
 */
export function axisRange(
    axis: TurretAxis, extentDegrees: number | null | undefined,
): TurretAxisRange {
    const extent = extentDegrees ?? 0;
    const free = !(extent > 0 && extent < CLAMP_BAND_MAX);

    return { axis, free, limitDegrees: free ? HALF_TURN : extent };
}

/** A turret the reader can drag, and the two bones that carry it. */
export interface TurretHandle {
    /** The weapon row that declared it, which is also the arc toggle's key. */
    id: string;
    partId: string;
    /** Carries the traverse. */
    turretBone: string;
    /** Carries the elevation, or null when one bone does both - 2 of 19 models. */
    barrelBone: string | null;
    yaw: TurretAxisRange;
    pitch: TurretAxisRange;
}

/** Where a turret is pointed, in degrees off the pose the model authored. */
export interface TurretAim {
    yaw: number;
    pitch: number;
}

/** Rest: the authored pose, which is what an untouched turret reads. */
export const TURRET_AT_REST: TurretAim = { yaw: 0, pitch: 0 };

/**
 * The handles that should be on screen, which is decided by the arc toggle and nothing else.
 *
 * A turret whose arc is SHOWN has handles. No new selection concept and no new control: the arc
 * state already exists at three levels - the stage pill, a toggle per weapon row, a fire bone in
 * the tree - and it is persistent by design, which hover is not. A manipulator you have to move the
 * pointer to cannot be gated on the pointer being somewhere else.
 */
export function turretHandles(
    sweepable: readonly TurretSweep[], shown: ReadonlySet<string>,
): TurretHandle[] {
    return sweepable
        .filter(sweep => shown.has(sweep.id))
        .map(sweep => ({
            id: sweep.id,
            partId: sweep.partId,
            turretBone: sweep.turretBone,
            barrelBone: sweep.barrelBone,
            yaw: axisRange('yaw', sweep.turret.rotateExtentDegrees),
            pitch: axisRange('pitch', sweep.turret.elevateExtentDegrees),
        }));
}

/** A weapon row, as far as the handles care: its id, the part its bones live on, its turret. */
export interface TurretWeapon {
    id: string;
    partId: string;
    turret?: PreviewTurret | null;
}

/**
 * The handles for the weapons whose arcs are shown - built from the WEAPONS, in their own ids.
 *
 * Not from the sweepable list. That one reaches a hardpoint turret through the HARDPOINT first and
 * keeps its key, `HP_Gargantuan_Main_Turret_Front`, while the arc toggle speaks the WEAPON's,
 * `hardpoint:HP_Gargantuan_Main_Turret_Front`. The two never matched, so no hardpoint turret could
 * ever get a handle - the Gargantuan showed twelve cones and no handles at all. The sweep keeps its
 * hardpoint key, because the sweep button on a hardpoint card speaks that one.
 *
 * The same gate and the same de-duplication by part and bone as the sweep, so the two can never
 * disagree about which turrets exist; the viewport matches a held handle against a sweep by BONE for
 * the same reason.
 */
export function handlesForShownArcs(
    weapons: readonly TurretWeapon[], shownWeaponIds: ReadonlySet<string>,
): TurretHandle[] {
    return turretHandles(turretSweeps([], weapons), shownWeaponIds);
}

/** One angle brought inside its own stops. A free axis wraps instead, because it has no stops. */
export function clampAxis(range: TurretAxisRange, degrees: number): number {
    if (range.free) {
        return wrap180(degrees);
    }

    return Math.min(Math.max(degrees, -range.limitDegrees), range.limitDegrees);
}

/** Both axes at once, which is what a drag produces. */
export function clampAim(handle: TurretHandle, aim: TurretAim): TurretAim {
    return {
        yaw: clampAxis(handle.yaw, aim.yaw),
        pitch: clampAxis(handle.pitch, aim.pitch),
    };
}

/**
 * Whether an axis is sitting ON its stop, which the readout has to say out loud.
 *
 * A handle that will not move is not self-explanatory - the reader cannot tell a stop from a
 * dropped pointer event. A free axis is never at a stop, because it has none.
 */
export function atStop(range: TurretAxisRange, degrees: number): boolean {
    return !range.free && Math.abs(Math.abs(degrees) - range.limitDegrees) < STOP_EPSILON;
}

/** Half a degree: closer than a drag can land, and closer than the readout can show. */
const STOP_EPSILON = 0.5;

/**
 * How far a drag turns a turret.
 *
 * Screen X drives yaw and screen Y drives pitch, with Y INVERTED so that dragging up raises the
 * barrel - the same convention the viewport already uses for elevation, where a positive angle is
 * nose up.
 */
export function dragToAim(
    handle: TurretHandle, start: TurretAim,
    deltaXPixels: number, deltaYPixels: number, degreesPerPixel: number,
): TurretAim {
    return clampAim(handle, {
        yaw: start.yaw + deltaXPixels * degreesPerPixel,
        pitch: start.pitch - deltaYPixels * degreesPerPixel,
    });
}

/**
 * What a row says about where its turret is pointed.
 *
 * Both axes always, even when one cannot move: a reader comparing two turrets needs the same two
 * numbers on each, and "free" is itself an answer.
 */
export function aimText(handle: TurretHandle, aim: TurretAim): string {
    return `${axisText('Yaw', handle.yaw, aim.yaw)}, ${axisText('Pitch', handle.pitch, aim.pitch)}`;
}

function axisText(label: string, range: TurretAxisRange, degrees: number): string {
    const value = `${label} ${round(degrees)} deg`;

    if (range.free) {
        return `${value} (free)`;
    }

    return atStop(range, degrees) ? `${value} (at stop)` : value;
}

/**
 * The two envelopes a TURRET hardpoint keeps, which are not the same shape.
 *
 * This is what manual control is for. `Can_Weapon_Point_At`'s turret branch tests the yaw extent
 * and NOTHING else - there is no pitch test on the shot at all - while
 * `Calculate_Desired_Turret_Angle` clamps both. So the barrel is elevation-limited and the shot is
 * not, which is the opposite way round from what the tag names suggest and is invisible unless the
 * two are drawn apart.
 */
export interface TurretEnvelopes {
    /** Where it may SHOOT. Yaw from the extent, pitch unbounded. */
    fire: { yawDegrees: number; pitchDegrees: number };
    /** Where the BARREL may point. Both axes clamped. */
    rotation: { yawDegrees: number; pitchDegrees: number };
}

/**
 * Both envelopes as FULL angles, which is what the arc geometry takes.
 *
 * The extents are plus-or-minus bounds and the geometry halves what it is given, so each one
 * doubles on the way out. That conversion is the single error this whole workstream kept finding in
 * three different places; it happens here, once.
 */
export function turretEnvelopes(
    handle: { yaw: TurretAxisRange; pitch: TurretAxisRange },
): TurretEnvelopes {
    const yaw = fullAngle(handle.yaw);

    return {
        fire: { yawDegrees: yaw, pitchDegrees: 360 },
        rotation: { yawDegrees: yaw, pitchDegrees: fullAngle(handle.pitch) },
    };
}

function fullAngle(range: TurretAxisRange): number {
    return Math.min(range.limitDegrees * 2, 360);
}

/** Into (-180, 180], the range the engine's own `Clamp_180` produces. */
function wrap180(degrees: number): number {
    const wrapped = ((degrees + 180) % 360 + 360) % 360 - 180;

    return wrapped === -180 ? 180 : wrapped;
}

/** A number as a person writes it, to one decimal - a drag lands on 32.4, not on 32.4172. */
function round(degrees: number): number {
    return Number.parseFloat(degrees.toFixed(1));
}
