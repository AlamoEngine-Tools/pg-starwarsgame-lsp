using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class XmlDiagnosticsPublisherTests
{
    // ── helpers ─────────────────────────────────────────────────────────────

    private static XmlTagDefinition MakeTag(string name, bool multipleAllowed = false)
    {
        return new XmlTagDefinition { Tag = name, ValueType = XmlValueType.Float, MultipleAllowed = multipleAllowed };
    }

    private static GameObjectTypeDefinition MakeType(string name)
    {
        return new GameObjectTypeDefinition { TypeName = name };
    }

    private static XmlDiagnosticsPublisher BuildPublisher(FakeSchemaProvider schema)
    {
        return new XmlDiagnosticsPublisher(null!, schema, new FakeValidatorRegistry(),
            NullLogger<XmlDiagnosticsPublisher>.Instance);
    }

    // ── duplicate detection ─────────────────────────────────────────────────

    [Fact]
    public void CollectDiagnostics_SingleOccurrence_NoDiagnostics()
    {
        var schema = new FakeSchemaProvider();
        schema.AddTag(MakeTag("Max_Speed"));
        var publisher = BuildPublisher(schema);

        var diags = publisher.CollectDiagnostics("""
                                                 <SpaceUnit>
                                                   <Max_Speed>500</Max_Speed>
                                                 </SpaceUnit>
                                                 """);

        Assert.Empty(diags);
    }

    [Fact]
    public void CollectDiagnostics_TwoDuplicateSingletons_BothFlaggedWithCrossReferences()
    {
        var schema = new FakeSchemaProvider();
        schema.AddTag(MakeTag("Max_Speed"));
        var publisher = BuildPublisher(schema);

        // Lines 2 and 3 (1-based) in the raw string below
        var diags = publisher.CollectDiagnostics("""
                                                 <SpaceUnit>
                                                   <Max_Speed>500</Max_Speed>
                                                   <Max_Speed>600</Max_Speed>
                                                 </SpaceUnit>
                                                 """);

        Assert.Equal(2, diags.Count);
        Assert.All(diags, d => Assert.Contains("Max_Speed", d.Message));
        Assert.All(diags, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        // Each diagnostic must mention the other occurrence's line
        Assert.Contains("3", diags[0].Message); // first occurrence references line 3
        Assert.Contains("2", diags[1].Message); // second occurrence references line 2
    }

    [Fact]
    public void CollectDiagnostics_ThreeDuplicateSingletons_EachReferencesOtherTwo()
    {
        var schema = new FakeSchemaProvider();
        schema.AddTag(MakeTag("Max_Speed"));
        var publisher = BuildPublisher(schema);

        var diags = publisher.CollectDiagnostics("""
                                                 <SpaceUnit>
                                                   <Max_Speed>100</Max_Speed>
                                                   <Max_Speed>200</Max_Speed>
                                                   <Max_Speed>300</Max_Speed>
                                                 </SpaceUnit>
                                                 """);

        Assert.Equal(3, diags.Count);
        // First occurrence: "Also at lines 3, 4."
        Assert.Contains("3", diags[0].Message);
        Assert.Contains("4", diags[0].Message);
    }

    [Fact]
    public void CollectDiagnostics_MultipleAllowedTag_NoDiagnostics()
    {
        var schema = new FakeSchemaProvider();
        schema.AddTag(MakeTag("SFXEvent_Attack_Hardpoint", true));
        var publisher = BuildPublisher(schema);

        var diags = publisher.CollectDiagnostics("""
                                                 <HardPoint>
                                                   <SFXEvent_Attack_Hardpoint>Sfx_A</SFXEvent_Attack_Hardpoint>
                                                   <SFXEvent_Attack_Hardpoint>Sfx_B</SFXEvent_Attack_Hardpoint>
                                                 </HardPoint>
                                                 """);

        Assert.Empty(diags);
    }

    [Fact]
    public void CollectDiagnostics_SameSingletonInTwoDifferentObjects_NoDiagnostics()
    {
        var schema = new FakeSchemaProvider();
        schema.AddTag(MakeTag("Max_Speed"));
        schema.AddType(MakeType("SpaceUnit"));
        var publisher = BuildPublisher(schema);

        // Two separate SpaceUnit objects, each with one Max_Speed — not duplicates
        var diags = publisher.CollectDiagnostics("""
                                                 <GameObjectFiles>
                                                   <SpaceUnit Name="UnitA">
                                                     <Max_Speed>500</Max_Speed>
                                                   </SpaceUnit>
                                                   <SpaceUnit Name="UnitB">
                                                     <Max_Speed>300</Max_Speed>
                                                   </SpaceUnit>
                                                 </GameObjectFiles>
                                                 """);

        Assert.Empty(diags);
    }

    [Fact]
    public void CollectDiagnostics_NonTypeRootMatchingTagName_NotValidatedAsTag()
    {
        var schema = new FakeSchemaProvider();
        // Hardpoints is a singleton tag in the schema but NOT a registered type
        schema.AddTag(MakeTag("Hardpoints"));
        var publisher = BuildPublisher(schema);

        // Root <Hardpoints> must not be validated as a singleton tag field
        var diags = publisher.CollectDiagnostics("""
                                                 <Hardpoints>
                                                   <Hardpoint></Hardpoint>
                                                 </Hardpoints>
                                                 """);

        Assert.Empty(diags);
    }

    [Fact]
    public void CollectDiagnostics_RootTypeCollidesWithTagName_NoFalsePositiveDuplicate()
    {
        var schema = new FakeSchemaProvider();
        schema.AddTag(MakeTag("Faction")); // Faction is also a singleton tag elsewhere
        schema.AddType(MakeType("Faction")); // but here it is a type container
        var publisher = BuildPublisher(schema);

        // Two Faction instances at root level — must not be flagged as duplicate singleton tags
        var diags = publisher.CollectDiagnostics("""
                                                 <Faction Name="EMPIRE">
                                                   <Rank>1</Rank>
                                                 </Faction>
                                                 <Faction Name="REBEL">
                                                   <Rank>2</Rank>
                                                 </Faction>
                                                 """);

        Assert.Empty(diags);
    }

    [Fact]
    public void CollectDiagnostics_DuplicateUnknownTag_NoDiagnostics()
    {
        var schema = new FakeSchemaProvider();
        // Tag not registered → unknown → no duplicate error
        var publisher = BuildPublisher(schema);

        var diags = publisher.CollectDiagnostics("""
                                                 <SpaceUnit>
                                                   <Unknown_Tag>1</Unknown_Tag>
                                                   <Unknown_Tag>2</Unknown_Tag>
                                                 </SpaceUnit>
                                                 """);

        Assert.Empty(diags);
    }
    // ── fakes ───────────────────────────────────────────────────────────────

    private sealed class FakeSchemaProvider : ISchemaProvider
    {
        private readonly Dictionary<string, XmlTagDefinition> _tags = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GameObjectTypeDefinition> _types = new(StringComparer.OrdinalIgnoreCase);

        public XmlTagDefinition? GetTag(string name)
        {
            return _tags.GetValueOrDefault(name);
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _)
        {
            return [];
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [.. _tags.Values];

        public GameObjectTypeDefinition? GetObjectType(string name)
        {
            return _types.GetValueOrDefault(name);
        }

        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [.. _types.Values];

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _)
        {
            return [];
        }

        public EnumDefinition? GetEnum(string _)
        {
            return null;
        }

        public IReadOnlyList<EnumDefinition> AllEnums => [];

        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }

        public void AddTag(XmlTagDefinition tag)
        {
            _tags[tag.Tag] = tag;
        }

        public void AddType(GameObjectTypeDefinition type)
        {
            _types[type.TypeName] = type;
        }
    }

    private sealed class FakeValidatorRegistry : IXmlValueValidatorRegistry
    {
        public XmlValidationResult Validate(XmlValueType _, string __, XmlTagDefinition ___)
        {
            return XmlValidationResult.Valid();
        }
    }
}