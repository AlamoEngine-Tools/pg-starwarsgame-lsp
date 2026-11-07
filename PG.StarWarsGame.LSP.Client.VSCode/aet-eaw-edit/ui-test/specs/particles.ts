// The model preview's particle systems as the PRV tests read them: through the page's debug handle on the
// viewport (`aetDebugViewport.particleProbe()`), because whether a field is full, drifts, stays frozen or is
// drawn back to front is a question about the simulation rather than the pixels. Run inside the preview.
import { browser } from "@wdio/globals";

export type V3 = [number, number, number];

export interface ProbedParticle {
    drawn: V3;
    velocity: V3;
    inward: V3;
    age: number;
    stretch: number;
}

export interface EmitterProbe {
    name: string;
    frozen: boolean;
    weather: boolean;
    drift: V3;
    live: number;
    drawn: number;
    throughEffect: boolean;
    position: { shape: string; sideLength: number };
    speed: { shape: string; sideLength: number };
    inwardSpeed: number;
    particles: ProbedParticle[];
    positionSum: V3;
    ageSum: number;
    drawDepths: number[] | null;
}

export interface Probe {
    camera: { x: number; y: number; z: number };
    systems: { id: string; emitters: EmitterProbe[] }[];
}

/** Every running system's emitters as their last frame left them. */
export async function probe(): Promise<Probe> {
    return browser.execute(() => {
        const viewport = (window as unknown as { aetDebugViewport?: { particleProbe(): unknown } }).aetDebugViewport;
        return (viewport?.particleProbe() ?? { camera: { x: 0, y: 0, z: 0 }, systems: [] }) as never;
    });
}

/** Every emitter of every running system, in order. */
export async function emitterProbes(): Promise<EmitterProbe[]> {
    return (await probe()).systems.flatMap((s) => s.emitters);
}

/** Waits until the emitters answer `test`, and returns them. */
export async function emittersWhen(
    test: (emitters: EmitterProbe[]) => boolean,
    timeout = 90_000,
    message = "The particle systems never got there",
): Promise<EmitterProbe[]> {
    let found: EmitterProbe[] = [];
    await browser.waitUntil(async () => test((found = await emitterProbes())), { timeout, interval: 100, timeoutMsg: message });
    return found;
}

/** Puts the camera at a position looking at a target. */
export async function cameraTo(position: V3, target: V3 = [0, 0, 0]): Promise<void> {
    await browser.execute(
        (pose: { p: number[]; t: number[] }) => {
            const { p, t } = pose;
            const viewport = (window as unknown as {
                aetDebugViewport?: { applyCameraPose(p: object, t: object): void };
            }).aetDebugViewport;
            viewport?.applyCameraPose({ x: p[0], y: p[1], z: p[2] }, { x: t[0], y: t[1], z: t[2] });
        },
        { p: position, t: target },
    );
}

export const mean = (values: number[]): number => values.reduce((a, b) => a + b, 0) / Math.max(1, values.length);

/** The least-squares slope of `ys` over `xs`. */
export function slope(xs: number[], ys: number[]): number {
    const mx = mean(xs);
    const my = mean(ys);
    let num = 0;
    let den = 0;
    for (let i = 0; i < xs.length; i++) {
        num += (xs[i] - mx) * (ys[i] - my);
        den += (xs[i] - mx) ** 2;
    }
    return den === 0 ? 0 : num / den;
}

/** Pearson's correlation of `xs` and `ys`. */
export function correlation(xs: number[], ys: number[]): number {
    const mx = mean(xs);
    const my = mean(ys);
    let sxy = 0;
    let sxx = 0;
    let syy = 0;
    for (let i = 0; i < xs.length; i++) {
        sxy += (xs[i] - mx) * (ys[i] - my);
        sxx += (xs[i] - mx) ** 2;
        syy += (ys[i] - my) ** 2;
    }
    return sxx === 0 || syy === 0 ? 0 : sxy / Math.sqrt(sxx * syy);
}

/** The positions of a frozen batch as a sorted list of rounded triples, so two frames compare as sets. */
export function positionSet(emitter: EmitterProbe): string[] {
    return emitter.particles.map((p) => p.drawn.map((v) => v.toFixed(3)).join(",")).sort();
}

export async function pause(ms: number): Promise<void> {
    await browser.pause(ms);
}
