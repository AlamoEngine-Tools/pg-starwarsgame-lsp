// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// A stand-in context for the layer tests. A test gives only what its layer reads; the keys are checked
// against the real context, so a misspelt member does not compile, and reading a member the test did
// not give fails by NAME rather than as an `undefined` somewhere further on.

import { type ViewportContext } from "./viewportContext";

/** The members a test may give, each in whatever stand-in shape the layer under test needs. */
export type StandInContext = { [K in keyof ViewportContext]?: unknown };

/** A context holding only `given`; any other member throws when the layer reads it. */
export function standInContext(given: StandInContext): ViewportContext {
    return new Proxy(given, {
        get(target, key) {
            if (typeof key === "string" && !(key in target)) {
                throw new Error(`The stand-in context has no '${key}'.`);
            }
            return Reflect.get(target, key);
        },
    }) as unknown as ViewportContext;
}
