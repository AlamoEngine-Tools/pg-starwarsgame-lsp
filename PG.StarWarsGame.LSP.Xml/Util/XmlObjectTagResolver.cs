// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Util;

/// <summary>
///     Generic tag resolver: walks the ancestor-type chain innermost-first, then falls back to
///     the global flat schema lookup. Handles singletons, type-container instances, and ability
///     sub-types uniformly. Type-specific resolvers (e.g. GameObjectTagResolver) can be added
///     to the <see cref="XmlTagResolver" /> dispatch table when needed.
/// </summary>
internal sealed class XmlObjectTagResolver : ITagResolver
{
    public XmlTagDefinition? Resolve(ISchemaProvider schema, string tagName, TagResolutionContext? context)
    {
        for (var ctx = context; ctx is not null; ctx = ctx.Parent)
        {
            var hit = schema.GetTagsForType(ctx.ObjectTypeName)
                .FirstOrDefault(t => t.Tag.Equals(tagName, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit;
        }

        return WithoutOwnerRestrictions(schema.GetTag(tagName));
    }

    /// <summary>
    ///     The flat entry with its owner-specific restrictions removed.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The flat lookup answers "is there a tag by this name anywhere", which is what keeps a
    ///         real tag written on the wrong element from being reported as unknown. It cannot
    ///         answer "what may this tag hold HERE": the definition it returns belongs to whichever
    ///         type happens to hold the name in the flat map.
    ///     </para>
    ///     <para>
    ///         <c>Activation_Style</c> is why this exists. Eleven ability types each accept exactly
    ///         one style, most abilities declare no <c>Activation_Style</c> of their own, and those
    ///         inherited a stranger's single allowed value - so nearly every ability in a file was
    ///         told it only supports <c>GROUND_ACTIVATED</c>. The same leak applies to a
    ///         <c>validationId</c>, which also states a rule one owner's validator makes.
    ///     </para>
    ///     <para>
    ///         Only the RESTRICTIONS are dropped. Type, reference kind and enum are properties of
    ///         the tag itself and stay, so a misplaced tag is still typed and still completes.
    ///     </para>
    /// </remarks>
    private static XmlTagDefinition? WithoutOwnerRestrictions(XmlTagDefinition? flat)
    {
        if (flat is null) return null;
        if (flat.AllowedValues.Count == 0 && flat.ValidationOverride is null) return flat;

        return flat with { AllowedValues = [], ValidationOverride = null };
    }
}