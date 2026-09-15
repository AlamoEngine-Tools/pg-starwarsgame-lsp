// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Tests.Util;

/// <summary>
///     An owner-specific restriction must not travel through the resolver's flat fallback.
/// </summary>
/// <remarks>
///     <para>
///         The resolver walks the context chain and then falls back to a flat, owner-agnostic
///         lookup, which exists so a real tag written on the wrong element still resolves instead of
///         being reported as unknown. That fallback returned whichever type happened to hold the
///         name in the flat map - narrowing and all.
///     </para>
///     <para>
///         <c>Activation_Style</c> is the case that exposed it. Eleven ability types each accept
///         exactly one style; most abilities declare no <c>Activation_Style</c> of their own and
///         inherit the general one. Those fell through to the flat entry and inherited somebody
///         else's single allowed value, so nearly every ability in a file was told it only supports
///         <c>GROUND_ACTIVATED</c>.
///     </para>
///     <para>
///         A restriction belongs to the (type, tag) pair that states it. Found through the chain it
///         applies; found by name alone it does not.
///     </para>
/// </remarks>
public sealed class FlatFallbackNarrowingTest
{
    private const string Tag = "Activation_Style";

    [Fact]
    public void The_owner_that_states_the_restriction_keeps_it()
    {
        var resolved = Resolve("GalacticSabotageAbility");

        Assert.Equal(["GROUND_ACTIVATED"], resolved!.AllowedValues);
        Assert.NotNull(resolved.ValidationOverride);
    }

    /// <summary>
    ///     A type that declares no <c>Activation_Style</c> reaches the flat entry - and must not
    ///     inherit the narrowing that entry happens to carry.
    /// </summary>
    [Fact]
    public void A_type_that_falls_back_does_not_inherit_it()
    {
        var resolved = Resolve("StunAbility");

        Assert.NotNull(resolved);
        Assert.Empty(resolved!.AllowedValues);
        Assert.Null(resolved.ValidationOverride);
    }

    /// <summary>
    ///     Falling back must still RESOLVE the tag. Returning nothing would hand it to the
    ///     unknown-tag rule, which would be a worse diagnostic than the one being fixed.
    /// </summary>
    [Fact]
    public void The_fallback_still_resolves_the_tag()
    {
        Assert.Equal(Tag, Resolve("StunAbility")!.Tag);
        Assert.Equal(XmlValueType.DynamicEnumValue, Resolve("StunAbility")!.ValueType);
    }

    /// <summary>
    ///     An ANCESTOR in the context chain is a legitimate owner, so its restriction still applies -
    ///     the chain is how a sub-object inherits its parent's tags.
    /// </summary>
    [Fact]
    public void An_ancestor_in_the_chain_still_applies_its_restriction()
    {
        var parent = new TagResolutionContext("GalacticSabotageAbility", 0, HtmlNode());
        var child = new TagResolutionContext("SomeSubObject", 1, HtmlNode(), parent);

        var resolved = XmlTagResolver.Resolve(new NarrowedSchema(), Tag, child);

        Assert.Equal(["GROUND_ACTIVATED"], resolved!.AllowedValues);
    }

    private static XmlTagDefinition? Resolve(string typeName)
    {
        return XmlTagResolver.Resolve(new NarrowedSchema(), Tag,
            new TagResolutionContext(typeName, 0, HtmlNode()));
    }

    private static HtmlNode HtmlNode()
    {
        return XmlUtility.CreateHtmlDocument("<Root/>").DocumentNode;
    }

    /// <summary>One type narrows the tag; the flat map holds that same narrowed definition.</summary>
    private sealed class NarrowedSchema : ISchemaProvider
    {
        private static readonly XmlTagDefinition Narrowed = new()
        {
            Tag = Tag,
            ValueType = XmlValueType.DynamicEnumValue,
            AllowedValues = ["GROUND_ACTIVATED"],
            ValidationOverride = new TagValidationOverride { ValidationId = "some-rule" }
        };

        public XmlTagDefinition? GetTag(string tagName)
        {
            return tagName.Equals(Tag, StringComparison.OrdinalIgnoreCase) ? Narrowed : null;
        }

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
        {
            return typeName.Equals("GalacticSabotageAbility", StringComparison.OrdinalIgnoreCase)
                ? [Narrowed]
                : [];
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName)
        {
            return [];
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [Narrowed];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

        public GameObjectTypeDefinition? GetObjectType(string typeName)
        {
            return null;
        }

        public EnumDefinition? GetEnum(string enumName)
        {
            return null;
        }

        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }
    }
}