// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.HoverStrategies;

internal sealed class TagNameHoverStrategy : IXmlHoverStrategy
{
    private readonly IFileTypeRegistry _fileTypeRegistry;

    public TagNameHoverStrategy(IFileTypeRegistry fileTypeRegistry)
    {
        _fileTypeRegistry = fileTypeRegistry;
    }

    public Hover? Handle(HoverContext ctx)
    {
        if (!ctx.IsOnTagName)
            return null;

        var typeDef = ctx.Schema.GetObjectType(ctx.Node.Name);
        typeDef ??= ctx.Schema.GetObjectType(XmlUtility.ToPascalCase(ctx.Node.Name));

        if (typeDef is null)
            if (TryResolveContainingAbilityType(ctx.Node, ctx, out var abilityTypeName))
                typeDef = ctx.Schema.GetObjectType(abilityTypeName);

        if (typeDef is null)
        {
            var fileTypes = _fileTypeRegistry.GetTypesForFile(ctx.DocumentUri);
            if (!fileTypes.IsEmpty)
            {
                var registeredType = fileTypes
                    .Select(t => ctx.Schema.GetObjectType(t))
                    .FirstOrDefault(t => t?.NameTag is not null);
                if (registeredType is not null && XmlUtility.GetDepth(ctx.Node) > 0)
                    typeDef = registeredType;
            }
        }

        if (typeDef is not null)
        {
            var resCtx = new TagResolutionContext(typeDef.TypeName, XmlUtility.GetDepth(ctx.Node), ctx.Node);
            var typedTagDef = XmlTagResolver.Resolve(ctx.Schema, ctx.Node.Name, resCtx);

            if (typedTagDef is not null)
                return HoverUtility.BuildTagHover(typeDef, typedTagDef, ctx.Node, ctx.Locale);

            return HoverUtility.BuildTypeHover(typeDef, ctx.Node, ctx.Locale);
        }

        var tagDef = ctx.Schema.GetTag(ctx.Node.Name);
        return tagDef is not null
            ? HoverUtility.BuildTagHover(tagDef, ctx.Node, ctx.Locale)
            : null;
    }

    private static bool TryResolveContainingAbilityType(HtmlNode node, HoverContext ctx, out string? abilityTypeName)
    {
        for (var n = node.ParentNode; n?.ParentNode != null; n = n.ParentNode)
        {
            var parentTag = ctx.Schema.GetTag(n.ParentNode.Name);
            if (parentTag?.ValueType == XmlValueType.AbilityDefinitionSubObjectList)
            {
                abilityTypeName = XmlUtility.ToPascalCase(n.Name);
                return true;
            }

            if (parentTag?.ValueType == XmlValueType.GuiActivatedAbilityDefinitionSubObjectList)
            {
                abilityTypeName = "UnitAbility";
                return true;
            }
        }

        abilityTypeName = null;
        return false;
    }
}
