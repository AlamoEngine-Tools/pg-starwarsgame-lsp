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

    public static List<RawTagDefinition> ParseTagFile(string yaml, ILogger? logger = null)
    {
        var file = Deserializer.Deserialize<YamlTagFile>(yaml);
        var result = new List<RawTagDefinition>(file.Tags.Count);
        foreach (var entry in file.Tags)
        {
            if (!Enum.TryParse<XmlValueType>(entry.Type, true, out var valueType))
            {
                logger?.LogWarning("Unknown tag type '{Type}' for tag '{Tag}' - skipping", entry.Type, entry.Tag);
                continue;
            }

            var rk = ReferenceKind.None;
            if (entry.ReferenceKind is not null && !Enum.TryParse(entry.ReferenceKind, true, out rk))
                logger?.LogWarning("Unknown referenceKind '{Kind}' for tag '{Tag}' - defaulting to None",
                    entry.ReferenceKind, entry.Tag);

            var st = TagSemanticType.Default;
            if (entry.SemanticType is not null && !Enum.TryParse(entry.SemanticType, true, out st))
                logger?.LogWarning("Unknown semanticType '{SemanticType}' for tag '{Tag}' - defaulting to Default",
                    entry.SemanticType, entry.Tag);

            var variantMode = VariantMode.Replace;
            if (entry.VariantMode is not null && !Enum.TryParse(entry.VariantMode, true, out variantMode))
            {
                logger?.LogWarning("Unknown variantMode '{VariantMode}' for tag '{Tag}' - defaulting to Replace",
                    entry.VariantMode, entry.Tag);
                variantMode = VariantMode.Replace;
            }

            TagValidationOverride? validationOverride = null;
            if (entry.ValidationOverride is { } vo && !string.IsNullOrEmpty(vo.ValidationId))
            {
                var mode = ValidationOverrideMode.Additive;
                if (vo.Mode is not null && !Enum.TryParse(vo.Mode, true, out mode))
                {
                    logger?.LogWarning(
                        "Unknown validationOverride mode '{Mode}' for tag '{Tag}' - defaulting to Additive",
                        vo.Mode, entry.Tag);
                    mode = ValidationOverrideMode.Additive;
                }

                var order = ValidationOverrideOrder.Append;
                if (vo.Order is not null && !Enum.TryParse(vo.Order, true, out order))
                {
                    logger?.LogWarning(
                        "Unknown validationOverride order '{Order}' for tag '{Tag}' - defaulting to Append",
                        vo.Order, entry.Tag);
                    order = ValidationOverrideOrder.Append;
                }

                validationOverride = new TagValidationOverride
                {
                    ValidationId = vo.ValidationId,
                    Mode = mode,
                    Order = order
                };
            }

            result.Add(new RawTagDefinition
            {
                Tag = entry.Tag,
                ValueType = valueType,
                ReferenceKind = rk,
                ReferenceType = entry.ReferenceType,
                EnumName = entry.EnumName,
                SemanticType = st,
                ValueGroups = ParseValueGroups(entry.ValueGroup),
                Deprecated = entry.Deprecated,
                AvailableSince = entry.AvailableSince,
                Description = entry.Description,
                Notes = entry.Notes,
                MultipleAllowed = entry.MultipleAllowed,
                VariantMode = variantMode,
                ValidationOverride = validationOverride
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
                Description = entry.Description,
                Notes = entry.Notes
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
                Notes = v.Notes,
                Deprecated = v.Deprecated,
                AvailableSince = v.AvailableSince,
                Groups = v.Groups
            });
        return new HardcodedReferenceSet
        {
            Name = file.Name,
            Description = file.Description,
            Notes = file.Notes,
            Deprecated = file.Deprecated,
            AvailableSince = file.AvailableSince,
            Values = values
        };
    }

    public static RawEnumDefinition ParseEnumFile(string yaml, ILogger? logger = null)
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

        var values = new List<RawEnumValueDefinition>(file.Values.Count);
        foreach (var v in file.Values)
        {
            List<RawParamDefinition>? paramDefs = null;
            if (v.Params is { Count: > 0 })
            {
                paramDefs = new List<RawParamDefinition>(v.Params.Count);
                foreach (var p in v.Params)
                {
                    if (!Enum.TryParse<XmlValueType>(p.Type, true, out var paramValueType))
                    {
                        logger?.LogWarning(
                            "Unknown param type '{Type}' at position {Position} for enum value '{Value}' - skipping",
                            p.Type, p.Position, v.Name);
                        continue;
                    }

                    var prk = ReferenceKind.None;
                    if (p.ReferenceKind is not null && !Enum.TryParse(p.ReferenceKind, true, out prk))
                        logger?.LogWarning(
                            "Unknown referenceKind '{Kind}' for param at position {Position} for enum value '{Value}' - defaulting to None",
                            p.ReferenceKind, p.Position, v.Name);

                    paramDefs.Add(new RawParamDefinition
                    {
                        Position = p.Position,
                        ValueType = paramValueType,
                        ReferenceKind = prk,
                        ReferenceType = p.ReferenceType,
                        EnumName = p.EnumName,
                        Optional = p.Optional,
                        Description = p.Description,
                        Notes = p.Notes
                    });
                }
            }

            values.Add(new RawEnumValueDefinition
            {
                Name = v.Name,
                Description = v.Description,
                Notes = v.Notes,
                Deprecated = v.Deprecated,
                Untested = v.Untested,
                AvailableSince = v.AvailableSince,
                Groups = v.Groups,
                Params = paramDefs?.Count > 0 ? paramDefs : null
            });
        }

        return new RawEnumDefinition
        {
            Name = file.Name,
            Kind = kind,
            IsBitfield = file.IsBitfield,
            SourceFile = file.SourceFile,
            Description = file.Description,
            Notes = file.Notes,
            Deprecated = file.Deprecated,
            AvailableSince = file.AvailableSince,
            Values = values
        };
    }

    private static IReadOnlyList<string> ParseValueGroups(object? raw)
    {
        return raw switch
        {
            string s when s.Length > 0 => [s],
            List<object> list => list.OfType<string>().ToList(),
            _ => []
        };
    }

    public static IReadOnlyList<MetafileDefinition> ParseMetafileFile(string yaml)
    {
        var file = Deserializer.Deserialize<YamlMetafileFile>(yaml);
        var result = new List<MetafileDefinition>(file.Metafiles.Count);
        foreach (var entry in file.Metafiles)
        {
            if (!Enum.TryParse<MetafileType>(entry.MetaFileType, true, out var metafileType))
                continue;

            var normalizedPath = entry.Path.Replace('\\', '/').ToLowerInvariant();
            result.Add(new MetafileDefinition(normalizedPath, metafileType, entry.Types));
        }

        return result;
    }
}