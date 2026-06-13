// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>Where an effective tag's value came from, relative to the object being resolved.</summary>
public enum VariantProvenance
{
    /// <summary>The object has no base; the tag is its own.</summary>
    Own,

    /// <summary>The value comes from a base object; the resolved object does not redefine it.</summary>
    Inherited,

    /// <summary>The resolved object redefines (replaces) a tag that a base also defined.</summary>
    Overridden,

    /// <summary>The resolved object's value was merged onto the base's value (list union).</summary>
    Merged,

    /// <summary>The resolved object defines a tag that no base defined.</summary>
    Added
}
