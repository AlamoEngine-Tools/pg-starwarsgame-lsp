// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Completion;

internal sealed class StandardTagNameStrategy : IXmlTagNameCompletionStrategy
{
    private readonly IFileTypeRegistry _fileTypeRegistry;

    public StandardTagNameStrategy(IFileTypeRegistry fileTypeRegistry)
    {
        _fileTypeRegistry = fileTypeRegistry;
    }

    public IEnumerable<CompletionItem> Handle(TagNameCompletionContext ctx)
    {
        if (ctx.IsStoryParser && string.Equals(ctx.EnclosingTag, "Event", StringComparison.OrdinalIgnoreCase))
            return [];

        return BuildTagNameCompletions(ctx);
    }

    private IEnumerable<CompletionItem> BuildTagNameCompletions(TagNameCompletionContext ctx)
    {
        var fileTypes = _fileTypeRegistry.GetTypesForFile(ctx.DocumentUri);

        IReadOnlyList<XmlTagDefinition> candidates;
        if (!fileTypes.IsEmpty)
        {
            var isMultiInstance = fileTypes.Any(t => ctx.Schema.GetObjectType(t)?.NameTag is not null);
            var expectedDepth = isMultiInstance ? 2 : 1;
            if (ctx.EnclosingDepth != expectedDepth)
            {
                var subItems = TryBuildSubObjectListCompletions(ctx.EnclosingTag, ctx.Prefix, ctx.Schema);
                if (subItems is not null) return subItems;

                var abilityType = ctx.Schema.GetObjectType(XmlUtility.ToPascalCase(ctx.EnclosingTag));
                if (abilityType is null) return [];
                candidates = ctx.Schema.GetTagsForType(abilityType.TypeName);
            }
            else
            {
                var tagsList = new List<XmlTagDefinition>();
                foreach (var typeName in fileTypes)
                    tagsList.AddRange(ctx.Schema.GetTagsForType(typeName));
                candidates = tagsList;
            }
        }
        else
        {
            var subItems = TryBuildSubObjectListCompletions(ctx.EnclosingTag, ctx.Prefix, ctx.Schema);
            if (subItems is not null) return subItems;

            var typeDef = ctx.Schema.GetObjectType(ctx.EnclosingTag)
                          ?? ctx.Schema.GetObjectType(XmlUtility.ToPascalCase(ctx.EnclosingTag));
            if (typeDef is null) return [];
            candidates = ctx.Schema.GetTagsForType(typeDef.TypeName);
        }

        var existingTags = CollectExistingChildTagNames(ctx.Text, ctx.EnclosingTag);

        return candidates
            .Where(t => t.MultipleAllowed || !existingTags.Contains(t.Tag))
            .Where(t => ctx.Prefix.Length == 0 || t.Tag.StartsWith(ctx.Prefix, StringComparison.OrdinalIgnoreCase))
            .Select(t => new CompletionItem
            {
                Label = t.Tag,
                Kind = CompletionItemKind.Property,
                InsertText = $"{t.Tag}>$0</{t.Tag}>",
                InsertTextFormat = InsertTextFormat.Snippet
            });
    }

    private static IEnumerable<CompletionItem>? TryBuildSubObjectListCompletions(
        string parentName, string prefix, ISchemaProvider schema)
    {
        var parentTagDef = schema.GetTag(parentName);

        if (parentTagDef?.ValueType == XmlValueType.GuiActivatedAbilityDefinitionSubObjectList)
        {
            const string label = "Unit_Ability";
            if (prefix.Length > 0 && !label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return [];
            return
            [
                new CompletionItem
                {
                    Label = label,
                    Kind = CompletionItemKind.Property,
                    InsertText = "Unit_Ability>\n    <Type>$0</Type>\n</Unit_Ability>",
                    InsertTextFormat = InsertTextFormat.Snippet
                }
            ];
        }

        if (parentTagDef?.ValueType == XmlValueType.AbilityDefinitionSubObjectList)
            return schema.AllObjectTypes
                .Where(t => t.TypeName.EndsWith("Ability", StringComparison.OrdinalIgnoreCase))
                .Select(t => XmlUtility.ToSnakeCase(t.TypeName))
                .Where(name => prefix.Length == 0 || name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(name => new CompletionItem
                {
                    Label = name,
                    Kind = CompletionItemKind.Property,
                    InsertText = $"{name} Name=\"$1\">\n    $0\n</{name}>",
                    InsertTextFormat = InsertTextFormat.Snippet
                });

        return null;
    }

    private static HashSet<string> CollectExistingChildTagNames(string text, string parentName)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(text);

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parent = doc.DocumentNode.SelectSingleNode($"//{parentName.ToLowerInvariant()}");
        if (parent is null) return result;

        foreach (var child in parent.ChildNodes)
            if (child.NodeType == HtmlNodeType.Element)
                result.Add(child.Name);
        return result;
    }
}