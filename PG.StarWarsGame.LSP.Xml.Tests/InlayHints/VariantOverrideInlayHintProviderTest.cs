// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.InlayHints;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Tests.InlayHints;

/// <summary>
///     #73: an overriding tag gets a compact marker naming what it displaced ("overrides 99"),
///     phrased like the summary hint on the Variant_Of_Existing_Type line. Informational only - the
///     untruncated value and the base tag itself live in the effective-object expansion.
/// </summary>
public sealed class VariantOverrideInlayHintProviderTest
{
    private const string BaseUri = "file:///b.xml";

    private static GameSymbol Sym(string id, string? baseId, string uri = "file:///u.xml")
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, "SpaceUnit",
            new FileOrigin(uri, 0, 0), null, baseId);
    }

    private static GameIndex IndexWith(params GameSymbol[] symbols)
    {
        return GameIndex.Empty with
        {
            WorkspaceDefinitions = symbols.ToImmutableDictionary(
                s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase)
        };
    }

    private static List<InlayHint> Hints(string text, string tagName, FakeTagSource source,
        FakeSchema schema, GameIndex index)
    {
        var hapDoc = XmlUtility.CreateHtmlDocument(text);
        var node = hapDoc.DocumentNode.Descendants()
            .First(n => n.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
        var tagDef = schema.GetTag(tagName)
                     ?? new XmlTagDefinition { Tag = tagName, ValueType = XmlValueType.Float };
        var line = XmlUtility.GetLine(node);
        var lineEnd = text.Split('\n')[line].TrimEnd('\r').Length;
        var ctx = new InlayHintContext("file:///u.xml", index, schema, hapDoc, node, tagDef,
            line, lineEnd);

        return new VariantOverrideInlayHintProvider(source).Handle(ctx).ToList();
    }

    [Fact]
    public void Handle_AnchorsTheHintInsideTheLine()
    {
        // int.MaxValue is out of range per the LSP spec; clients clamp it when rendering. Keep the
        // anchor inside the line so the hint is well-formed regardless of how a client positions it.
        const string text =
            """<X><SpaceUnit Name="V"><Variant_Of_Existing_Type>B</Variant_Of_Existing_Type><Tech_Level>0</Tech_Level></SpaceUnit></X>""";
        var schema = new FakeSchema().Variant("Variant_Of_Existing_Type").Plain("Tech_Level");
        var source = new FakeTagSource()
            .With("V", Tag("Variant_Of_Existing_Type", "B", "file:///u.xml"), Tag("Tech_Level", "0", "file:///u.xml"))
            .With("B", Tag("Tech_Level", "99", BaseUri, 7));

        var hint = Assert.Single(Hints(text, "Tech_Level", source, schema,
            IndexWith(Sym("V", "B"), Sym("B", null, BaseUri))));

        Assert.True(hint.Position.Character <= text.Split('\n')[hint.Position.Line].TrimEnd('\r').Length,
            $"hint anchored past the end of its line at character {hint.Position.Character}");
    }

    private static string SingleLabel(List<InlayHint> hints)
    {
        var hint = Assert.Single(hints);
        var label = hint.Label.String;
        Assert.NotNull(label);
        return label!;
    }

    [Fact]
    public void Handle_OverriddenTag_NamesTheValueItReplaced()
    {
        const string text =
            """<X><SpaceUnit Name="V"><Variant_Of_Existing_Type>B</Variant_Of_Existing_Type><Tech_Level>0</Tech_Level></SpaceUnit></X>""";
        var schema = new FakeSchema().Variant("Variant_Of_Existing_Type").Plain("Tech_Level");
        var source = new FakeTagSource()
            .With("V", Tag("Variant_Of_Existing_Type", "B", "file:///u.xml"), Tag("Tech_Level", "0", "file:///u.xml"))
            .With("B", Tag("Tech_Level", "99", BaseUri, 7));

        var label = SingleLabel(Hints(text, "Tech_Level", source, schema,
            IndexWith(Sym("V", "B"), Sym("B", null, BaseUri))));

        Assert.Equal("overrides 99", label);
    }

    [Fact]
    public void Handle_LongInheritedValue_IsTruncated()
    {
        // Inherited values are routinely long comma-separated lists; the marker must not push the
        // line off-screen. The full text is in the effective-object expansion.
        var longValue = string.Join(", ", Enumerable.Range(0, 20).Select(i => $"Entry_{i}"));
        const string text =
            """<X><SpaceUnit Name="V"><Variant_Of_Existing_Type>B</Variant_Of_Existing_Type><Tech_Level>0</Tech_Level></SpaceUnit></X>""";
        var schema = new FakeSchema().Variant("Variant_Of_Existing_Type").Plain("Tech_Level");
        var source = new FakeTagSource()
            .With("V", Tag("Variant_Of_Existing_Type", "B", "file:///u.xml"), Tag("Tech_Level", "0", "file:///u.xml"))
            .With("B", Tag("Tech_Level", longValue, BaseUri, 7));

        var label = SingleLabel(Hints(text, "Tech_Level", source, schema,
            IndexWith(Sym("V", "B"), Sym("B", null, BaseUri))));

        Assert.EndsWith("…", label);
        Assert.True(label.Length < 50, $"marker too long for an inline hint: '{label}'");
    }

    [Fact]
    public void Handle_SingleOccurrenceMergeTag_NamesTheValueItMergesWith()
    {
        // Not multipleAllowed: the tag stays one element whose tokens are unioned, so there is a
        // single base value to name rather than a count of inherited entries.
        const string text =
            """<X><SpaceUnit Name="V"><Variant_Of_Existing_Type>B</Variant_Of_Existing_Type><Death_Clone>Hero</Death_Clone></SpaceUnit></X>""";
        var schema = new FakeSchema().Variant("Variant_Of_Existing_Type")
            .Plain("Death_Clone", VariantMode.Merge);
        var source = new FakeTagSource()
            .With("V", Tag("Variant_Of_Existing_Type", "B", "file:///u.xml"), Tag("Death_Clone", "Hero", "file:///u.xml"))
            .With("B", Tag("Death_Clone", "Base", BaseUri, 3));

        var label = SingleLabel(Hints(text, "Death_Clone", source, schema,
            IndexWith(Sym("V", "B"), Sym("B", null, BaseUri))));

        Assert.Equal("merges with Base", label);
    }

    [Fact]
    public void Handle_RepeatableAdditiveTag_CountsInheritedEntries()
    {
        // A multipleAllowed additive tag resolves to one entry per occurrence, the base's inherited
        // ones first - so matching merely on tag name would find an Inherited entry and emit nothing.
        // Naming one of several inherited entries would be arbitrary, so the marker counts them.
        const string text =
            """<X><SpaceUnit Name="V"><Variant_Of_Existing_Type>B</Variant_Of_Existing_Type><Death_Clone>Damage_Fire, B_Clone</Death_Clone></SpaceUnit></X>""";
        var schema = new FakeSchema().Variant("Variant_Of_Existing_Type")
            .Plain("Death_Clone", VariantMode.Merge, true);
        var source = new FakeTagSource()
            .With("V", Tag("Variant_Of_Existing_Type", "B", "file:///u.xml"),
                Tag("Death_Clone", "Damage_Fire, B_Clone", "file:///u.xml"))
            .With("B", Tag("Death_Clone", "Damage_Fire, A_Clone", BaseUri, 3),
                Tag("Death_Clone", "Damage_Force_Lightning, A_Lightning", BaseUri, 4));

        var label = SingleLabel(Hints(text, "Death_Clone", source, schema,
            IndexWith(Sym("V", "B"), Sym("B", null, BaseUri))));

        Assert.Equal("adds to 2 inherited", label);
    }

    [Fact]
    public void Handle_BaseTagInShippedData_StillEmitsMarker()
    {
        // A baseline tag carries a game-relative path the editor cannot open; the marker must still
        // convey "this overrides something" rather than vanishing.
        const string text =
            """<X><SpaceUnit Name="V"><Variant_Of_Existing_Type>B</Variant_Of_Existing_Type><Tech_Level>0</Tech_Level></SpaceUnit></X>""";
        var schema = new FakeSchema().Variant("Variant_Of_Existing_Type").Plain("Tech_Level");
        var source = new FakeTagSource()
            .With("V", Tag("Variant_Of_Existing_Type", "B", "file:///u.xml"), Tag("Tech_Level", "0", "file:///u.xml"))
            .With("B", Tag("Tech_Level", "99", "DATA\\XML\\UNITS.XML", 7));

        var label = SingleLabel(Hints(text, "Tech_Level", source, schema,
            IndexWith(Sym("V", "B"), Sym("B", null, BaseUri))));

        Assert.Equal("overrides 99", label);
    }

    [Fact]
    public void Handle_AddedTag_NoMarker()
    {
        const string text =
            """<X><SpaceUnit Name="V"><Variant_Of_Existing_Type>B</Variant_Of_Existing_Type><Shield>50</Shield></SpaceUnit></X>""";
        var schema = new FakeSchema().Variant("Variant_Of_Existing_Type").Plain("Shield");
        var source = new FakeTagSource()
            .With("V", Tag("Variant_Of_Existing_Type", "B", "file:///u.xml"), Tag("Shield", "50", "file:///u.xml"))
            .With("B", Tag("Tech_Level", "99", BaseUri));

        Assert.Empty(Hints(text, "Shield", source, schema, IndexWith(Sym("V", "B"), Sym("B", null, BaseUri))));
    }

    [Fact]
    public void Handle_ObjectWithoutVariantParent_NoMarker()
    {
        const string text = """<X><SpaceUnit Name="P"><Tech_Level>3</Tech_Level></SpaceUnit></X>""";
        var schema = new FakeSchema().Variant("Variant_Of_Existing_Type").Plain("Tech_Level");
        var source = new FakeTagSource().With("P", Tag("Tech_Level", "3", "file:///u.xml"));

        Assert.Empty(Hints(text, "Tech_Level", source, schema, IndexWith(Sym("P", null))));
    }

    [Fact]
    public void Handle_VariantParentTagItself_NoMarker()
    {
        const string text =
            """<X><SpaceUnit Name="V"><Variant_Of_Existing_Type>B</Variant_Of_Existing_Type><Tech_Level>0</Tech_Level></SpaceUnit></X>""";
        var schema = new FakeSchema().Variant("Variant_Of_Existing_Type").Plain("Tech_Level");
        var source = new FakeTagSource()
            .With("V", Tag("Variant_Of_Existing_Type", "B", "file:///u.xml"), Tag("Tech_Level", "0", "file:///u.xml"))
            .With("B", Tag("Tech_Level", "99", BaseUri));

        Assert.Empty(Hints(text, "Variant_Of_Existing_Type", source, schema,
            IndexWith(Sym("V", "B"), Sym("B", null, BaseUri))));
    }

    private static VariantTag Tag(string name, string value, string uri, int line = 0)
    {
        return new VariantTag(name, value, $"<{name}>{value}</{name}>", line, new FileOrigin(uri, line, null));
    }

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

        public FakeSchema Variant(string tag)
        {
            _tags[tag] = new XmlTagDefinition
            {
                Tag = tag, ValueType = XmlValueType.TypeReference,
                ReferenceKind = ReferenceKind.XmlObject, SemanticType = TagSemanticType.VariantParent
            };
            return this;
        }

        public FakeSchema Plain(string tag, VariantMode mode = VariantMode.Replace,
            bool multipleAllowed = false)
        {
            _tags[tag] = new XmlTagDefinition
            {
                Tag = tag, ValueType = XmlValueType.Float, VariantMode = mode,
                MultipleAllowed = multipleAllowed
            };
            return this;
        }
    }
}
