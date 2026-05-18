// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validators;

file sealed class CompletenessSchemaProvider : ISchemaProvider
{
    public EnumDefinition? GetEnum(string _) => null;
    public IReadOnlyList<EnumDefinition> AllEnums => [];
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
    public IReadOnlyList<XmlTagDefinition> AllTags => [];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public XmlTagDefinition? GetTag(string _) => null;
    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _) => [];
    public GameObjectTypeDefinition? GetObjectType(string _) => null;
    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _) => [];
    public event EventHandler? SchemaRefreshed { add { } remove { } }
}

[Trait("Category", "FeatureCompleteness")]
public sealed class ValidatorCompletenessTest
{
    private static readonly IReadOnlySet<XmlValueType> CoveredValueTypes;
    private static readonly IReadOnlySet<TagSemanticType> CoveredSemanticTypes;

    static ValidatorCompletenessTest()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISchemaProvider>(new CompletenessSchemaProvider());
        services.AddXmlLanguageServices();
        using var provider = services.BuildServiceProvider();
        var validators = provider.GetRequiredService<IEnumerable<IXmlValueValidator>>().ToList();
        CoveredValueTypes = validators
            .Where(v => v.SemanticType == TagSemanticType.Default)
            .Select(v => v.ValueType)
            .ToHashSet();
        CoveredSemanticTypes = validators
            .Where(v => v.SemanticType != TagSemanticType.Default)
            .Select(v => v.SemanticType)
            .ToHashSet();
    }

    // ── Value-type completeness ───────────────────────────────────────────────

    /// <summary>
    ///     Types listed here are permanently excluded from the completeness check.
    ///     Add an entry (with a comment) when a deliberate decision is made not to validate a type.
    ///     [Obsolete]-tagged members of <see cref="XmlValueType" /> are excluded automatically.
    /// </summary>
    private static readonly HashSet<XmlValueType> ExcludedValueTypes =
    [
        // Example: XmlValueType.SomeType,  // reason why we will never validate this
    ];

    private static bool AcceptValueType(XmlValueType t)
    {
        if (ExcludedValueTypes.Contains(t))
            return false;

        if (typeof(XmlValueType).GetField(t.ToString())
                ?.GetCustomAttribute<ObsoleteAttribute>() is not null)
            return false;

        return true;
    }

    public static IEnumerable<object[]> AcceptedValueTypes =>
        Enum.GetValues<XmlValueType>().Where(AcceptValueType).Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(AcceptedValueTypes))]
    public void Each_accepted_value_type_has_a_default_validator(XmlValueType type)
    {
        Assert.True(CoveredValueTypes.Contains(type),
            $"No IXmlValueValidator for XmlValueType.{type}. " +
            "Implement a validator or add it to ExcludedValueTypes with a comment explaining the decision.");
    }

    // ── Semantic-type completeness ────────────────────────────────────────────

    /// <summary>
    ///     Semantic types listed here are permanently excluded from the completeness check.
    ///     <see cref="TagSemanticType.Default" /> is always excluded (it is the base case, not a dedicated slot).
    ///     <see cref="TagSemanticType.FlagList" /> is excluded because it is routed to the value-type validator
    ///     via the registry fallback rather than requiring its own dedicated validator.
    /// </summary>
    private static readonly HashSet<TagSemanticType> ExcludedSemanticTypes =
    [
        TagSemanticType.Default,   // base case — every value-type validator covers Default implicitly
        TagSemanticType.FlagList,  // routed to value-type validator via XmlValueValidatorRegistry fallback
    ];

    private static bool AcceptSemanticType(TagSemanticType t) => !ExcludedSemanticTypes.Contains(t);

    public static IEnumerable<object[]> AcceptedSemanticTypes =>
        Enum.GetValues<TagSemanticType>().Where(AcceptSemanticType).Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(AcceptedSemanticTypes))]
    public void Each_semantic_type_has_a_registered_validator(TagSemanticType semanticType)
    {
        Assert.True(CoveredSemanticTypes.Contains(semanticType),
            $"No dedicated IXmlValueValidator with SemanticType.{semanticType}. " +
            "Implement one or add it to ExcludedSemanticTypes with a comment explaining the decision.");
    }
}
