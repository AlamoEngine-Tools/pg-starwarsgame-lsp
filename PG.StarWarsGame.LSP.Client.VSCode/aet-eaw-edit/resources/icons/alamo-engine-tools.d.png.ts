// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The type of `alamo-engine-tools.png` when it is IMPORTED, which the preview does to embed the
// missing-texture marker: esbuild's `dataurl` loader turns the file into a `data:` URI string.
//
// This shape - a sibling `.d.<ext>.ts` plus `allowArbitraryExtensions` - is TypeScript's own
// mechanism for a non-code import. A `declare module '*.png'` wildcard does NOT work here, because
// the path is relative and the file really exists: TypeScript resolves it, finds something it
// cannot read as a module, and never consults the wildcard.

declare const uri: string;
export default uri;
