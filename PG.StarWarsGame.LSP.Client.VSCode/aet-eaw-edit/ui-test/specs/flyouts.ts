// Where a stage flyout sits against the pill that opens it, as the PRV and STY tests read it.
import { browser } from "@wdio/globals";

/** The gap the flyout keeps to its pill's plate, and the margin it keeps to the stage's edge. */
const GAP = 8;
const MARGIN = 8;

/** What a browser rounds away between two boxes laid out from fractional sizes. */
const SLACK = 1.5;

interface Box {
    left: number;
    right: number;
    top: number;
    bottom: number;
}

export interface FlyoutPlacement {
    /** The button that opens the flyout. */
    pill: Box;
    /** The plate the pill sits on, or the pill itself when it has none. */
    plate: Box;
    flyout: Box;
    /** The tail that points from the flyout at its pill, or null when it has none. */
    tail: Box | null;
    /** The box the flyout is placed in, inside its border. */
    stage: Box;
}

/** The boxes of an open flyout and its pill, or null while either is missing. Run inside the panel. */
export async function flyoutPlacement(pill: string, flyout: string): Promise<FlyoutPlacement | null> {
    return browser.execute(
        (pillSelector: string, label: string) => {
            const button = document.querySelector<HTMLElement>(pillSelector);
            const box = Array.from(document.querySelectorAll<HTMLElement>(".stage-flyout")).find(
                (f) => f.getAttribute("aria-label") === label,
            );
            const stage = box?.offsetParent;
            if (!button || !box || !(stage instanceof HTMLElement)) {
                return null;
            }
            const of = (el: Element): { left: number; right: number; top: number; bottom: number } => {
                const r = el.getBoundingClientRect();
                return { left: r.left, right: r.right, top: r.top, bottom: r.bottom };
            };
            const outer = stage.getBoundingClientRect();
            const tail = box.querySelector(".stage-flyout-tail");
            return {
                pill: of(button),
                plate: of(button.closest(".stage-chrome") ?? button),
                flyout: of(box),
                tail: tail ? of(tail) : null,
                stage: {
                    left: outer.left + stage.clientLeft,
                    top: outer.top + stage.clientTop,
                    right: outer.left + stage.clientLeft + stage.clientWidth,
                    bottom: outer.top + stage.clientTop + stage.clientHeight,
                },
            };
        },
        pill,
        flyout,
    );
}

/** Presses a pill, waits for its flyout and returns where both are. Run inside the panel. */
export async function openFlyout(pill: string, flyout: string): Promise<FlyoutPlacement> {
    const button = await browser.$(pill);
    await button.waitForExist({ timeout: 120_000 });
    await browser.waitUntil(async () => button.isEnabled(), { timeout: 30_000, timeoutMsg: `${pill} stays disabled` });
    if ((await button.getAttribute("aria-expanded")) !== "true") {
        await browser.execute((el: HTMLElement) => el.click(), button as unknown as HTMLElement);
    }
    let at: FlyoutPlacement | null = null;
    await browser.waitUntil(async () => (at = await flyoutPlacement(pill, flyout)) !== null, {
        timeout: 10_000,
        timeoutMsg: `The ${flyout} flyout did not open`,
    });
    // One more read after the layout has settled: the flyout is measured, then placed.
    await browser.pause(300);
    at = await flyoutPlacement(pill, flyout);
    console.log(`[flyout] ${flyout}: ${JSON.stringify(at)}`);
    return at!;
}

/**
 * What is wrong with where a flyout sits, or nothing. It opens away from the stage edge its pill is on,
 * the gap clear of the pill's plate, and centred on the pill unless the stage's side edge stops it first.
 */
export function misplaced(name: string, at: FlyoutPlacement): string[] {
    const wrong: string[] = [];
    const near = (a: number, b: number): boolean => Math.abs(a - b) <= SLACK;
    const pillMiddle = (at.pill.left + at.pill.right) / 2;
    const flyoutMiddle = (at.flyout.left + at.flyout.right) / 2;
    const onTopEdge = (at.pill.top + at.pill.bottom) / 2 <= (at.stage.top + at.stage.bottom) / 2;

    if (onTopEdge && !near(at.flyout.top, at.plate.bottom + GAP)) {
        wrong.push(`${name}: opens ${Math.round(at.flyout.top - at.plate.bottom)} px below its pill's plate, not ${GAP}`);
    }
    if (!onTopEdge && !near(at.flyout.bottom, at.plate.top - GAP)) {
        wrong.push(`${name}: opens ${Math.round(at.plate.top - at.flyout.bottom)} px above its pill's plate, not ${GAP}`);
    }

    const stoppedLeft = near(at.flyout.left, at.stage.left + MARGIN) && pillMiddle < flyoutMiddle;
    const stoppedRight = near(at.flyout.right, at.stage.right - MARGIN) && pillMiddle > flyoutMiddle;
    if (!near(pillMiddle, flyoutMiddle) && !stoppedLeft && !stoppedRight) {
        wrong.push(`${name}: its middle is ${Math.round(flyoutMiddle - pillMiddle)} px off its pill's`);
    }
    return wrong;
}

/**
 * What is wrong with a flyout's tail, or nothing. It sits on the edge of the flyout that faces the pill, and
 * its middle is over the pill's middle, so it points at that pill and at no neighbour of it.
 */
export function tailOff(name: string, at: FlyoutPlacement): string[] {
    if (at.tail === null) {
        return [`${name}: has no tail`];
    }
    const wrong: string[] = [];
    const near = (a: number, b: number): boolean => Math.abs(a - b) <= SLACK;
    const pillMiddle = (at.pill.left + at.pill.right) / 2;
    const tailMiddle = (at.tail.left + at.tail.right) / 2;
    const tailCentre = (at.tail.top + at.tail.bottom) / 2;
    const onTopEdge = (at.pill.top + at.pill.bottom) / 2 <= (at.stage.top + at.stage.bottom) / 2;
    const facing = onTopEdge ? at.flyout.top : at.flyout.bottom;

    if (!near(tailMiddle, pillMiddle)) {
        wrong.push(`${name}: its tail is ${Math.round(tailMiddle - pillMiddle)} px off its pill's middle`);
    }
    if (!near(tailCentre, facing)) {
        wrong.push(`${name}: its tail is ${Math.round(tailCentre - facing)} px off the edge that faces its pill`);
    }
    return wrong;
}
