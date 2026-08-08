// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Which param rows a node body shows, and in what order.

import { StoryParamSchemaDto } from '../../protocol';

/** One editable param row on an inline node body. `missing` = mandatory and still unset. */
export interface ParamRowSpec {
    position: number;
    value: string;
    missing: boolean;
}

/**
 * Rows to render for one param kind on a node body.
 *
 * EVERY schema-declared param appears - the type dictates the fields, and an optional one renders
 * as an empty "(optional)" slot rather than being absent, so the shape of a node says what the type
 * takes. Any value present in the XML beyond what the schema declares is appended: a legacy or
 * unknown slot still has to be visible and editable, since hiding it would make an edit elsewhere
 * silently drop it.
 *
 * Shared between rendering and node-height estimation, so the two can never disagree about how tall
 * a node actually is - which showed up as the last field clipping against the bottom border.
 */
export function paramRowSpecs(
    existing: { position: number; value: string }[] | null | undefined,
    schema: StoryParamSchemaDto[],
): ParamRowSpec[] {
    const byPosition = new Map((existing ?? []).map(p => [p.position, p.value]));
    const rows: ParamRowSpec[] = [];

    for (const p of schema) {
        rows.push({
            position: p.position,
            value: byPosition.get(p.position) ?? '',
            missing: !p.optional && !byPosition.has(p.position),
        });
        // Taken out as it is consumed, so what is left below is exactly the undeclared slots.
        byPosition.delete(p.position);
    }

    for (const [position, value] of byPosition) {
        // Never "missing": the schema says nothing about it, so it cannot be a mandatory gap.
        rows.push({ position, value, missing: false });
    }

    rows.sort((a, b) => a.position - b.position);
    return rows;
}
