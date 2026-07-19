// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Tests.Symbols;

public sealed class EffectiveObjectResolverTest
{
    private const string Type = "SpaceUnit";

    // ── helpers ──────────────────────────────────────────────────────────────

    private static GameSymbol Sym(string id, string? variantBaseId = null)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, Type,
            new FileOrigin($"file:///{id}.xml", 0, 0), null, variantBaseId);
    }

    private static VariantTag Tag(string name, string value)
    {
        return new VariantTag(name, value, $"<{name}>{value}</{name}>", 0);
    }

    private static GameIndex WorkspaceIndex(params GameSymbol[] symbols)
    {
        var defs = symbols
            .GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .ToImmutableDictionary(g => g.Key, g => g.ToImmutableArray(), StringComparer.OrdinalIgnoreCase);
        return GameIndex.Empty with { WorkspaceDefinitions = defs };
    }

    private static EffectiveObjectResolver Resolver(GameIndex index, FakeTagSource source, FakeSchema? schema = null)
    {
        return new EffectiveObjectResolver(index, schema ?? new FakeSchema(), source);
    }

    private static EffectiveTag TagNamed(EffectiveObject o, string name)
    {
        return o.Tags.Single(t => t.TagName == name);
    }

    // ── not found ────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_UnknownId_NotFound()
    {
        var result = Resolver(GameIndex.Empty, new FakeTagSource()).Resolve("MISSING");

        Assert.False(result.Found);
    }

    // ── plain object (no base) ───────────────────────────────────────────────

    [Fact]
    public void Resolve_PlainObject_ReturnsOwnTags()
    {
        var index = WorkspaceIndex(Sym("OBJ"));
        var source = new FakeTagSource().With("OBJ", Tag("Max_Health", "100"), Tag("Mass", "5"));

        var result = Resolver(index, source).Resolve("OBJ");

        Assert.True(result.Found);
        Assert.Equal(2, result.Tags.Length);
        Assert.All(result.Tags, t => Assert.Equal(VariantProvenance.Own, t.Provenance));
        Assert.Equal(["OBJ"], result.Chain);
    }

    // ── single-level inheritance ─────────────────────────────────────────────

    [Fact]
    public void Resolve_Variant_InheritsBaseTag_NotRedefined()
    {
        var index = WorkspaceIndex(Sym("V", "B"), Sym("B"));
        var source = new FakeTagSource()
            .With("B", Tag("Max_Health", "100"))
            .With("V", Tag("Mass", "5"));

        var result = Resolver(index, source).Resolve("V");

        var health = TagNamed(result, "Max_Health");
        Assert.Equal("100", health.Value);
        Assert.Equal(VariantProvenance.Inherited, health.Provenance);
        Assert.Equal("B", health.OriginObjectId);
    }

    [Fact]
    public void Resolve_Variant_OverridesBaseTag()
    {
        var index = WorkspaceIndex(Sym("V", "B"), Sym("B"));
        var source = new FakeTagSource()
            .With("B", Tag("Max_Health", "100"))
            .With("V", Tag("Max_Health", "250"));

        var result = Resolver(index, source).Resolve("V");

        var health = TagNamed(result, "Max_Health");
        Assert.Equal("250", health.Value);
        Assert.Equal(VariantProvenance.Overridden, health.Provenance);
        Assert.Equal("V", health.OriginObjectId);
    }

    // ── base value carried alongside the winning value (#73 / #63) ───────────

    [Fact]
    public void Resolve_OverriddenTag_CarriesTheValueItReplaced()
    {
        // The UX has to be able to say "tech_level 0 (was 99)", so the replaced value must survive
        // resolution rather than being silently overwritten.
        var index = WorkspaceIndex(Sym("V", "B"), Sym("B"));
        var source = new FakeTagSource()
            .With("B", Tag("Tech_Level", "99"))
            .With("V", Tag("Tech_Level", "0"));

        var result = Resolver(index, source).Resolve("V");

        var tech = TagNamed(result, "Tech_Level");
        Assert.Equal("0", tech.Value);
        Assert.Equal("99", tech.BaseValue);
    }

    [Fact]
    public void Resolve_MergedTag_CarriesTheBaseValueItWasUnionedWith()
    {
        var index = WorkspaceIndex(Sym("V", "B"), Sym("B"));
        var source = new FakeTagSource()
            .With("B", Tag("Death_Clone", "Damage_Normal, Base_Clone"))
            .With("V", Tag("Death_Clone", "Hero_Clone"));
        var schema = new FakeSchema().WithMode("Death_Clone", VariantMode.Merge);

        var result = Resolver(index, source, schema).Resolve("V");

        var clone = TagNamed(result, "Death_Clone");
        Assert.Equal("Damage_Normal, Base_Clone, Hero_Clone", clone.Value);
        Assert.Equal("Damage_Normal, Base_Clone", clone.BaseValue);
    }

    [Fact]
    public void Resolve_AddedAndInheritedTags_HaveNoBaseValue()
    {
        // Nothing was replaced in either case, so there is no "was X" to report.
        var index = WorkspaceIndex(Sym("V", "B"), Sym("B"));
        var source = new FakeTagSource()
            .With("B", Tag("Max_Health", "100"))
            .With("V", Tag("Shield", "50"));

        var result = Resolver(index, source).Resolve("V");

        Assert.Null(TagNamed(result, "Shield").BaseValue);
        Assert.Null(TagNamed(result, "Max_Health").BaseValue);
    }

    [Fact]
    public void Resolve_ThreeLayerChain_BaseValueIsTheOneImmediatelyReplaced()
    {
        // A > B > C: the value V replaces is B's, not the root C's - "was" must mean the value the
        // object actually inherited, not the oldest one in the chain.
        var index = WorkspaceIndex(Sym("A", "B"), Sym("B", "C"), Sym("C"));
        var source = new FakeTagSource()
            .With("C", Tag("Tech_Level", "1"))
            .With("B", Tag("Tech_Level", "2"))
            .With("A", Tag("Tech_Level", "3"));

        var result = Resolver(index, source).Resolve("A");

        var tech = TagNamed(result, "Tech_Level");
        Assert.Equal("3", tech.Value);
        Assert.Equal("2", tech.BaseValue);
    }

    [Fact]
    public void Resolve_MultipleAllowedMergeTag_KeepsEachOccurrenceSeparate()
    {
        // Death_Clone is a (DamageType, CloneObject) pair, one element per damage type. Unioning the
        // tokens of every occurrence into one flat list drops repeated damage types as "duplicates",
        // which silently detaches the surviving clone names from their damage type. Each element has
        // to survive as its own tag.
        var index = WorkspaceIndex(Sym("V", "B"), Sym("B"));
        var source = new FakeTagSource()
            .With("B",
                Tag("Death_Clone", "Damage_Fire, A_Fire_Clone"),
                Tag("Death_Clone", "Damage_Force_Lightning, A_Lightning_Clone"))
            .With("V",
                Tag("Death_Clone", "Damage_Fire, B_Fire_Clone"),
                Tag("Death_Clone", "Damage_Force_Lightning, B_Lightning_Clone"));
        var schema = new FakeSchema().WithMode("Death_Clone", VariantMode.Merge).WithMultiple("Death_Clone");

        var result = Resolver(index, source, schema).Resolve("V");

        var clones = result.Tags.Where(t => t.TagName == "Death_Clone").ToList();
        Assert.Equal(4, clones.Count);
        Assert.Equal(
            ["Damage_Fire, A_Fire_Clone", "Damage_Force_Lightning, A_Lightning_Clone",
                "Damage_Fire, B_Fire_Clone", "Damage_Force_Lightning, B_Lightning_Clone"],
            clones.Select(c => c.Value));
        // The base's own entries are inherited; only the variant's are the additive ones.
        Assert.Equal(
            [VariantProvenance.Inherited, VariantProvenance.Inherited,
                VariantProvenance.Merged, VariantProvenance.Merged],
            clones.Select(c => c.Provenance));
    }

    [Fact]
    public void Resolve_SingleOccurrenceMergeTag_StillUnionsTokens()
    {
        // A flat list-valued tag that is not multipleAllowed keeps the union semantics.
        var index = WorkspaceIndex(Sym("V", "B"), Sym("B"));
        var source = new FakeTagSource()
            .With("B", Tag("Tags", "A, B"))
            .With("V", Tag("Tags", "C"));
        var schema = new FakeSchema().WithMode("Tags", VariantMode.Merge);

        var result = Resolver(index, source, schema).Resolve("V");

        Assert.Equal("A, B, C", TagNamed(result, "Tags").Value);
    }

    [Fact]
    public void Resolve_Variant_AddsNewTag()
    {
        var index = WorkspaceIndex(Sym("V", "B"), Sym("B"));
        var source = new FakeTagSource()
            .With("B", Tag("Max_Health", "100"))
            .With("V", Tag("Shield", "50"));

        var result = Resolver(index, source).Resolve("V");

        Assert.Equal(VariantProvenance.Added, TagNamed(result, "Shield").Provenance);
    }

    // ── merge mode ───────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_MergeMode_UnionsValues()
    {
        var index = WorkspaceIndex(Sym("V", "B"), Sym("B"));
        var source = new FakeTagSource()
            .With("B", Tag("Tags", "A, B"))
            .With("V", Tag("Tags", "C"));
        var schema = new FakeSchema().WithMode("Tags", VariantMode.Merge);

        var result = Resolver(index, source, schema).Resolve("V");

        var merged = TagNamed(result, "Tags");
        Assert.Equal(VariantProvenance.Merged, merged.Provenance);
        Assert.Equal("A, B, C", merged.Value);
    }

    // ── ignored mode ─────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_IgnoredMode_VariantCannotOverride_BaseValueKept()
    {
        var index = WorkspaceIndex(Sym("V", "B"), Sym("B"));
        var source = new FakeTagSource()
            .With("B", Tag("Locked", "base"))
            .With("V", Tag("Locked", "ignored"));
        var schema = new FakeSchema().WithMode("Locked", VariantMode.Ignored);

        var result = Resolver(index, source, schema).Resolve("V");

        var locked = TagNamed(result, "Locked");
        Assert.Equal("base", locked.Value);
        Assert.Equal(VariantProvenance.Inherited, locked.Provenance);
    }

    [Fact]
    public void Resolve_IgnoredMode_VariantCannotAdd_TagDropped()
    {
        var index = WorkspaceIndex(Sym("V", "B"), Sym("B"));
        var source = new FakeTagSource()
            .With("B", Tag("Max_Health", "100"))
            .With("V", Tag("Locked", "nope"));
        var schema = new FakeSchema().WithMode("Locked", VariantMode.Ignored);

        var result = Resolver(index, source, schema).Resolve("V");

        Assert.DoesNotContain(result.Tags, t => t.TagName == "Locked");
    }

    // ── multi-level chain ────────────────────────────────────────────────────

    [Fact]
    public void Resolve_ThreeLevelChain_AppliesInnermostFirst()
    {
        var index = WorkspaceIndex(Sym("Top", "Mid"), Sym("Mid", "Root"), Sym("Root"));
        var source = new FakeTagSource()
            .With("Root", Tag("A", "root"), Tag("B", "root"), Tag("C", "root"))
            .With("Mid", Tag("B", "mid"))
            .With("Top", Tag("C", "top"));

        var result = Resolver(index, source).Resolve("Top");

        Assert.Equal("root", TagNamed(result, "A").Value); // inherited from root
        Assert.Equal(VariantProvenance.Inherited, TagNamed(result, "A").Provenance);
        Assert.Equal("mid", TagNamed(result, "B").Value); // overridden at mid, inherited by top
        Assert.Equal(VariantProvenance.Inherited, TagNamed(result, "B").Provenance);
        Assert.Equal("top", TagNamed(result, "C").Value); // overridden at top
        Assert.Equal(VariantProvenance.Overridden, TagNamed(result, "C").Provenance);
        Assert.Equal(["Top", "Mid", "Root"], result.Chain);
    }

    // ── baseline base ────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_BaselineBase_MergesWorkspaceVariantOnShippedObject()
    {
        var baseSym = Sym("SHIPPED");
        var baseline = BaselineIndex.Empty with
        {
            Symbols = ImmutableDictionary<string, GameSymbol>.Empty.Add("SHIPPED", baseSym),
            ObjectTags = ImmutableDictionary<string, ImmutableArray<BaselineTag>>.Empty
                .Add("SHIPPED", [new BaselineTag("Max_Health", "100", "<Max_Health>100</Max_Health>", 2)])
        };
        var index = GameIndex.Empty with
        {
            Baseline = baseline,
            WorkspaceDefinitions = ImmutableDictionary.Create<string, ImmutableArray<GameSymbol>>(
                    StringComparer.OrdinalIgnoreCase)
                .Add("V", [Sym("V", "SHIPPED")])
        };
        var source = new FakeTagSource().With("V", Tag("Max_Health", "999"));

        var result = Resolver(index, source).Resolve("V");

        var health = TagNamed(result, "Max_Health");
        Assert.Equal("999", health.Value);
        Assert.Equal(VariantProvenance.Overridden, health.Provenance);
    }

    // ── cycle detection ──────────────────────────────────────────────────────

    [Fact]
    public void Resolve_DirectCycle_FlaggedCyclic()
    {
        var index = WorkspaceIndex(Sym("A", "A"));
        var source = new FakeTagSource().With("A", Tag("X", "1"));

        var result = Resolver(index, source).Resolve("A");

        Assert.True(result.Cyclic);
        Assert.Equal("A", result.CycleObjectId);
    }

    [Fact]
    public void Resolve_IndirectCycle_FlaggedCyclic()
    {
        var index = WorkspaceIndex(Sym("A", "B"), Sym("B", "A"));
        var source = new FakeTagSource().With("A", Tag("X", "1")).With("B", Tag("Y", "2"));

        var result = Resolver(index, source).Resolve("A");

        Assert.True(result.Cyclic);
    }

    // ── variant marker excluded ──────────────────────────────────────────────

    [Fact]
    public void Resolve_ExcludesVariantOfExistingTypeTagItself()
    {
        var index = WorkspaceIndex(Sym("V", "B"), Sym("B"));
        var source = new FakeTagSource()
            .With("B", Tag("Max_Health", "100"))
            .With("V", Tag("Variant_Of_Existing_Type", "B"));
        var schema = new FakeSchema().WithVariantParent("Variant_Of_Existing_Type");

        var result = Resolver(index, source, schema).Resolve("V");

        Assert.DoesNotContain(result.Tags, t => t.TagName == "Variant_Of_Existing_Type");
        Assert.Contains(result.Tags, t => t.TagName == "Max_Health");
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeTagSource : IVariantTagSource
    {
        private readonly Dictionary<string, IReadOnlyList<VariantTag>> _byId =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<VariantTag>? TryGetTags(string objectId)
        {
            return _byId.GetValueOrDefault(objectId);
        }

        public FakeTagSource With(string id, params VariantTag[] tags)
        {
            _byId[id] = tags;
            return this;
        }
    }

    private sealed class FakeSchema : ISchemaProvider
    {
        private readonly Dictionary<string, XmlTagDefinition> _tags = new(StringComparer.OrdinalIgnoreCase);

        public XmlTagDefinition? GetTag(string tagName)
        {
            return _tags.GetValueOrDefault(tagName);
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName)
        {
            return [];
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [];

        public GameObjectTypeDefinition? GetObjectType(string typeName)
        {
            return null;
        }

        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
        {
            return [];
        }

        public EnumDefinition? GetEnum(string enumName)
        {
            return null;
        }

        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }

        public FakeSchema WithMode(string tag, VariantMode mode)
        {
            _tags[tag] = new XmlTagDefinition { Tag = tag, ValueType = XmlValueType.NameReference, VariantMode = mode };
            return this;
        }

        public FakeSchema WithMultiple(string tag)
        {
            var existing = _tags.GetValueOrDefault(tag)
                           ?? new XmlTagDefinition { Tag = tag, ValueType = XmlValueType.NameReference };
            _tags[tag] = existing with { MultipleAllowed = true };
            return this;
        }

        public FakeSchema WithVariantParent(string tag)
        {
            _tags[tag] = new XmlTagDefinition
            {
                Tag = tag, ValueType = XmlValueType.TypeReference,
                ReferenceKind = ReferenceKind.XmlObject, SemanticType = TagSemanticType.VariantParent
            };
            return this;
        }
    }
}