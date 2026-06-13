// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

/// <summary>
///     Describes how a tag on a variant GameObject (one declaring
///     <c>Variant_Of_Existing_Type</c>) composes with the same tag on its base object.
/// </summary>
public enum VariantMode
{
    /// <summary>
    ///     Default: the variant's value replaces the base's value (a classic override). When the
    ///     base does not define the tag, the variant's value is simply added.
    /// </summary>
    Replace = 0,

    /// <summary>
    ///     The variant's value list is unioned/appended onto the base's list rather than replacing it.
    ///     Typically applies to list-valued tags.
    /// </summary>
    Merge,

    /// <summary>
    ///     The engine ignores this tag on variants; it can be inherited from the base but cannot be
    ///     overridden or added by a variant. Setting it on a variant is flagged by diagnostics.
    /// </summary>
    Ignored
}
