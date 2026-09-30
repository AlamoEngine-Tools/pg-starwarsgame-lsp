// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

/**
 * Whether the card draws the population blip at its top-left.
 *
 * ZERO is not a population. The engine guards the whole blip - the disc, the number and the offset
 * it pushes the name by - on the value being non-zero, and a zero also selects the header band
 * variant with no disc baked into it. Treating "declared" as "draw it" put a blip reading `0` on
 * every object that writes `<Population_Value>0</Population_Value>`, which the game never shows.
 *
 * A NEGATIVE value is still a population; the game draws it in a different colour rather than
 * hiding it, so it must not be folded into the zero case.
 *
 * Split out of the card so it can be tested without a DOM, like the rest of the webview's logic.
 */
export function drawsPopulationBlip(populationValue: number | null | undefined): boolean {
    return populationValue !== null && populationValue !== undefined && populationValue !== 0;
}
