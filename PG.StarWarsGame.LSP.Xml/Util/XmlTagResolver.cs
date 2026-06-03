// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Util;

/// <summary>
/// Dispatcher: selects the appropriate <see cref="ITagResolver"/> based on the file-level
/// object type (the root of the context chain, determined from the file's metafile registration)
/// and delegates resolution to it.
/// </summary>
internal static class XmlTagResolver
{
    private static readonly ITagResolver Default = new XmlObjectTagResolver();

    public static XmlTagDefinition? Resolve(
        ISchemaProvider schema, string tagName, TagResolutionContext? context)
        => SelectResolver(context).Resolve(schema, tagName, context);

    private static ITagResolver SelectResolver(TagResolutionContext? context)
    {
        if (context is null) return Default;

        // Walk to the root context node — that's the file-level type
        // (e.g. "GameObjectType" for spaceunitsfrigates.xml, "SFXEvent" for an SFX file,
        //  a singleton type name for files like GameConstants etc.).
        var root = context;
        while (root.Parent is not null) root = root.Parent;

        return root.ObjectTypeName switch
        {
            // Future type-specific resolvers:
            // "GameObjectType" or "SpaceUnit" => GameObjectTagResolver.Instance,
            // "SFXEvent"                      => SFXEventTagResolver.Instance,
            _ => Default
        };
    }
}
