// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Validation;
using PG.StarWarsGame.LSP.Xml.Validation;
using PG.StarWarsGame.LSP.Xml.Validation.Validators;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation;

file sealed class StubSchemaProvider : ISchemaProvider
{
    public EnumDefinition? GetEnum(string _) => null;
    public IReadOnlyList<EnumDefinition> AllEnums => [];
    public IReadOnlyList<XmlTagDefinition> AllTags => [];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public XmlTagDefinition? GetTag(string _) => null;
    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _) => [];
    public GameObjectTypeDefinition? GetObjectType(string _) => null;
    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _) => [];
    public event EventHandler? SchemaRefreshed { add { } remove { } }
}

public sealed class XmlValueValidatorRegistryTest
{
    [Fact]
    public void Validate_falls_back_to_value_type_validator_when_no_semantic_validator_is_registered()
    {
        // Without fallback: the registry returns a debug hint (IsValid=true in DEBUG, false in RELEASE)
        // for any unknown semantic type, regardless of the value.
        // With fallback: the value-type validator runs and rejects the empty string.
        // Using an invalid value that the validator always rejects lets us distinguish the two paths
        // even in DEBUG mode where the hint also returns IsValid=true.
        var validators = new IXmlValueValidator[] { new DynamicEnumValueValidator(new StubSchemaProvider()) };
        var registry = new XmlValueValidatorRegistry(validators);
        var tag = new XmlTagDefinition
        {
            Tag = "CategoryMask",
            ValueType = XmlValueType.DynamicEnumValue,
            SemanticType = TagSemanticType.FlagList
        };

        var result = registry.Validate(XmlValueType.DynamicEnumValue, "", tag);

        Assert.False(result.IsValid);
    }
}
