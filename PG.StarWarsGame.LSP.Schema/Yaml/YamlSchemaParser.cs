// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Yaml.YamlType;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PG.StarWarsGame.LSP.Schema.Yaml;

internal static class YamlSchemaParser
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static List<XmlTagDefinition> ParseTagFile(string yaml, ILogger? logger = null)
    {
        var file = Deserializer.Deserialize<YamlTagFile>(yaml);
        var result = new List<XmlTagDefinition>(file.Tags.Count);
        foreach (var entry in file.Tags)
        {
            if (!Enum.TryParse<XmlValueType>(entry.Type, true, out var valueType))
            {
                logger?.LogWarning("Unknown tag type '{Type}' for tag '{Tag}' — skipping", entry.Type, entry.Tag);
                continue;
            }

            var rk = ReferenceKind.None;
            if (entry.ReferenceKind is not null && !Enum.TryParse(entry.ReferenceKind, true, out rk))
                logger?.LogWarning("Unknown referenceKind '{Kind}' for tag '{Tag}' — defaulting to None",
                    entry.ReferenceKind, entry.Tag);

            var st = TagSemanticType.Default;
            if (entry.SemanticType is not null && !Enum.TryParse(entry.SemanticType, true, out st))
                logger?.LogWarning("Unknown semanticType '{SemanticType}' for tag '{Tag}' — defaulting to Default",
                    entry.SemanticType, entry.Tag);

            result.Add(new XmlTagDefinition
            {
                Tag = entry.Tag,
                ValueType = valueType,
                ReferenceKind = rk,
                ReferenceType = entry.ReferenceType,
                EnumName = entry.EnumName,
                SemanticType = st,
                ValueGroup = entry.ValueGroup,
                Deprecated = entry.Deprecated,
                AvailableSince = entry.AvailableSince,
                Description = entry.Description,
                MultipleAllowed = entry.MultipleAllowed
            });
        }

        return result;
    }

    public static List<GameObjectTypeDefinition> ParseTypeFile(string yaml)
    {
        var file = Deserializer.Deserialize<YamlTypeFile>(yaml);
        var result = new List<GameObjectTypeDefinition>(file.Types.Count);
        foreach (var entry in file.Types)
            result.Add(new GameObjectTypeDefinition
            {
                TypeName = entry.TypeName,
                NameTag = entry.NameTag,
                Description = entry.Description
            });
        return result;
    }

    public static HardcodedReferenceSet ParseHardcodedSetFile(string yaml)
    {
        var file = Deserializer.Deserialize<YamlHardcodedSetFile>(yaml);
        var values = new List<HardcodedReferenceSetValue>(file.Values.Count);
        foreach (var v in file.Values)
            values.Add(new HardcodedReferenceSetValue
            {
                Name = v.Name,
                Description = v.Description,
                Deprecated = v.Deprecated,
                AvailableSince = v.AvailableSince,
                Groups = v.Groups
            });
        return new HardcodedReferenceSet
        {
            Name = file.Name,
            Description = file.Description,
            Deprecated = file.Deprecated,
            AvailableSince = file.AvailableSince,
            Values = values
        };
    }

    public static EnumDefinition ParseEnumFile(string yaml, ILogger? logger = null)
    {
        var file = Deserializer.Deserialize<YamlEnumFile>(yaml);
        var kind = Enum.TryParse<EnumKind>(file.Kind, true, out var k)
            ? k
            : EnumKind.SchemaFixed;

        if (kind is EnumKind.DynamicXml or EnumKind.GameConstants)
        {
            if (string.IsNullOrEmpty(file.SourceFile))
                logger?.LogWarning("DynamicXml enum '{Name}' is missing sourceFile", file.Name);
            if (file.Values.Count > 0)
                logger?.LogWarning(
                    "DynamicXml enum '{Name}' should not define values in schema; {Count} value(s) will be ignored",
                    file.Name, file.Values.Count);
        }

        var values = new List<EnumValueDefinition>(file.Values.Count);
        foreach (var v in file.Values)
            values.Add(new EnumValueDefinition
            {
                Name = v.Name,
                Description = v.Description,
                Deprecated = v.Deprecated,
                AvailableSince = v.AvailableSince,
                Groups = v.Groups
            });
        return new EnumDefinition
        {
            Name = file.Name,
            Kind = kind,
            IsBitfield = file.IsBitfield,
            SourceFile = file.SourceFile,
            Description = file.Description,
            Deprecated = file.Deprecated,
            AvailableSince = file.AvailableSince,
            Values = values
        };
    }
}