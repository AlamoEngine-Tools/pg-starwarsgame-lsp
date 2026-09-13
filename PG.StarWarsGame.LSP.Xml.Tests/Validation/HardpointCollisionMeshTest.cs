// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Util;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation;

/// <summary>
///     A destroyable hardpoint nothing can ever hit.
/// </summary>
/// <remarks>
///     <para>
///         Damage is routed to a hardpoint by its collision mesh, and the lookup returns the FIRST
///         match - both measured in the damage-routing pass over the 2018 binary, not stated by any
///         engine message. So two shapes make a hardpoint undamageable while the game says nothing
///         at all: declaring it destroyable with no collision mesh, and giving two destroyable
///         hardpoints on one object the same mesh, where only the first can ever be reached.
///     </para>
///     <para>
///         Measured on the shipped corpus: 363 hardpoint definitions, 266 of them destroyable, and
///         every one names a collision mesh; across 140 objects and 1,081 destroyable attachments,
///         no object gives two of them the same mesh. Silent on vanilla, and exercised by it.
///     </para>
/// </remarks>
public sealed class HardpointCollisionMeshTest
{
    private const string Uri = "file:///hardpoints.xml";

    [Fact]
    public void A_destroyable_hardpoint_with_no_collision_mesh_is_reported()
    {
        const string text = """<X><HardPoint Name="HP_A"><Is_Destroyable>Yes</Is_Destroyable>""" +
                            """<Attachment_Bone>HP_Bone_A</Attachment_Bone></HardPoint></X>""";

        var fact = Assert.Single(Produce(text, new HardpointTagSource(), Sym("HP_A", "HardPoint"))
            .OfType<HardpointUnhittableFact>());

        Assert.Equal("HP_A", fact.HardpointId);
        Assert.Null(fact.SharedWith);
    }

    [Fact]
    public void A_destroyable_hardpoint_with_a_mesh_is_silent()
    {
        const string text = """<X><HardPoint Name="HP_A"><Is_Destroyable>Yes</Is_Destroyable>""" +
                            """<Collision_Mesh>HP_A_COLL</Collision_Mesh></HardPoint></X>""";

        Assert.Empty(Produce(text, new HardpointTagSource(), Sym("HP_A", "HardPoint"))
            .OfType<HardpointUnhittableFact>());
    }

    /// <summary>
    ///     An indestructible hardpoint is never a damage target, so it has no use for a collision
    ///     mesh and its absence says nothing.
    /// </summary>
    [Theory]
    [InlineData("No")]
    [InlineData("")]
    public void An_indestructible_hardpoint_needs_no_mesh(string destroyable)
    {
        var text = """<X><HardPoint Name="HP_A">""" +
                   (destroyable.Length == 0
                       ? ""
                       : $"<Is_Destroyable>{destroyable}</Is_Destroyable>") +
                   """<Attachment_Bone>HP_Bone_A</Attachment_Bone></HardPoint></X>""";

        Assert.Empty(Produce(text, new HardpointTagSource(), Sym("HP_A", "HardPoint"))
            .OfType<HardpointUnhittableFact>());
    }

    /// <summary>
    ///     Two destroyable hardpoints on one object claiming the same mesh: the first wins the
    ///     lookup and the second is dead weight.
    /// </summary>
    [Fact]
    public void Two_destroyable_hardpoints_sharing_a_mesh_are_reported()
    {
        const string text = """<X><SpaceUnit Name="SHIP"><HardPoints>HP_A, HP_B</HardPoints>""" +
                            """</SpaceUnit></X>""";

        var source = new HardpointTagSource()
            .With("HP_A", new VariantTag("Is_Destroyable", "Yes", "", 0),
                new VariantTag("Collision_Mesh", "SHARED_COLL", "", 0))
            .With("HP_B", new VariantTag("Is_Destroyable", "Yes", "", 0),
                new VariantTag("Collision_Mesh", "SHARED_COLL", "", 0));

        var fact = Assert.Single(Produce(text, source, Sym("SHIP", "SpaceUnit"))
            .OfType<HardpointUnhittableFact>());

        // Reported on the one that loses the lookup, naming the one that wins it.
        Assert.Equal("HP_B", fact.HardpointId);
        Assert.Equal("HP_A", fact.SharedWith);
    }

    /// <summary>Matching is case-insensitive, as every other name comparison here is.</summary>
    [Fact]
    public void The_mesh_comparison_ignores_case()
    {
        const string text = """<X><SpaceUnit Name="SHIP"><HardPoints>HP_A, HP_B</HardPoints>""" +
                            """</SpaceUnit></X>""";

        var source = new HardpointTagSource()
            .With("HP_A", new VariantTag("Is_Destroyable", "Yes", "", 0),
                new VariantTag("Collision_Mesh", "shared_coll", "", 0))
            .With("HP_B", new VariantTag("Is_Destroyable", "Yes", "", 0),
                new VariantTag("Collision_Mesh", "SHARED_COLL", "", 0));

        Assert.Single(Produce(text, source, Sym("SHIP", "SpaceUnit")).OfType<HardpointUnhittableFact>());
    }

    /// <summary>
    ///     Only DESTROYABLE hardpoints compete for the lookup, so an indestructible one sharing the
    ///     mesh takes nothing away from the one that matters.
    /// </summary>
    [Fact]
    public void An_indestructible_sibling_does_not_collide()
    {
        const string text = """<X><SpaceUnit Name="SHIP"><HardPoints>HP_A, HP_B</HardPoints>""" +
                            """</SpaceUnit></X>""";

        var source = new HardpointTagSource()
            .With("HP_A", new VariantTag("Is_Destroyable", "No", "", 0),
                new VariantTag("Collision_Mesh", "SHARED_COLL", "", 0))
            .With("HP_B", new VariantTag("Is_Destroyable", "Yes", "", 0),
                new VariantTag("Collision_Mesh", "SHARED_COLL", "", 0));

        Assert.Empty(Produce(text, source, Sym("SHIP", "SpaceUnit")).OfType<HardpointUnhittableFact>());
    }

    [Fact]
    public void Different_meshes_are_silent()
    {
        const string text = """<X><SpaceUnit Name="SHIP"><HardPoints>HP_A, HP_B</HardPoints>""" +
                            """</SpaceUnit></X>""";

        var source = new HardpointTagSource()
            .With("HP_A", new VariantTag("Is_Destroyable", "Yes", "", 0),
                new VariantTag("Collision_Mesh", "A_COLL", "", 0))
            .With("HP_B", new VariantTag("Is_Destroyable", "Yes", "", 0),
                new VariantTag("Collision_Mesh", "B_COLL", "", 0));

        Assert.Empty(Produce(text, source, Sym("SHIP", "SpaceUnit")).OfType<HardpointUnhittableFact>());
    }

    private static IReadOnlyList<XmlFact> Produce(string text, IVariantTagSource source,
        params GameSymbol[] symbols)
    {
        var docSymbols = symbols.ToImmutableArray();
        var docIndex = new DocumentIndex(Uri, 1, docSymbols, ImmutableArray<GameReference>.Empty);
        var defs = symbols.ToImmutableDictionary(s => s.Id, s => ImmutableArray.Create(s),
            StringComparer.OrdinalIgnoreCase);
        var index = GameIndex.Empty with
        {
            Documents = ImmutableDictionary<string, DocumentIndex>.Empty.Add(Uri, docIndex),
            WorkspaceDefinitions = defs,
            // Non-empty so the producer does not bail out as "no models catalogued at all".
            ModelBones = ImmutableDictionary<string, ImmutableArray<string>>.Empty
                .Add("ship.alo", ["Root"]),
        };

        return new XmlHardpointFactProducer(new BareSchema(), source)
            .Produce(Uri, ParsedXmlDocument.Parse(text), index);
    }

    private static GameSymbol Sym(string id, string typeName)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName, new FileOrigin(Uri, 0, 0), null, null);
    }

    private sealed class HardpointTagSource : IVariantTagSource
    {
        private readonly Dictionary<string, List<VariantTag>> _tags = new(StringComparer.OrdinalIgnoreCase);

        public HardpointTagSource With(string id, params VariantTag[] tags)
        {
            _tags[id] = [.. tags];
            return this;
        }

        public IReadOnlyList<VariantTag>? TryGetTags(string objectId)
        {
            return _tags.GetValueOrDefault(objectId);
        }
    }

    private sealed class BareSchema : ISchemaProvider
    {
        public XmlTagDefinition? GetTag(string tagName)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName)
        {
            return [];
        }

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
        {
            return [];
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [];
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
