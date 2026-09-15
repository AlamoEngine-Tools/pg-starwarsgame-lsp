// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

/**
 * Chooses the values a filter dropdown offers.
 *
 * A facet list answers "what could I switch to", which is a question about the CAMPAIGN - so it
 * cannot be derived from the nodes a filtered request returned. Doing that degenerates exactly
 * where the control matters: filter to one branch and that branch becomes the only option left,
 * so the user has to clear the filter and start again to reach any other one.
 *
 * The server resolves the filter and therefore knows the full set; it now sends it. `fromNodes`
 * stays only as the degraded path for a server that predates the facet fields, where re-adding
 * `active` at least keeps the select showing its own value instead of rendering blank - which
 * would read as "no filter" while a filter is applied.
 *
 * @param fromServer Unfiltered facet from the server, or undefined if it did not send one. An
 *                   EMPTY array is an answer ("none exist"), not a missing field.
 * @param fromNodes  Values observed on the returned - filtered - nodes.
 * @param active     The currently selected value, if any.
 */
export function facetList(
    fromServer: string[] | undefined | null,
    fromNodes: (string | null | undefined)[],
    active: string,
): string[] {
    if (fromServer) { return fromServer; }

    const found = [...new Set(fromNodes.filter((v): v is string => !!v))].sort();
    return active && !found.includes(active) ? [...found, active].sort() : found;
}
