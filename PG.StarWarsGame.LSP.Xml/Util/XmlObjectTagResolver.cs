// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Util;

/// <summary>
/// Generic tag resolver: walks the ancestor-type chain innermost-first, then falls back to
/// the global flat schema lookup. Handles singletons, type-container instances, and ability
/// sub-types uniformly. Type-specific resolvers (e.g. GameObjectTagResolver) can be added
/// to the <see cref="XmlTagResolver"/> dispatch table when needed.
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

        return schema.GetTag(tagName);
    }
}
