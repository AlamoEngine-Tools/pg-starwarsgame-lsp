// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Projection;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Assets.Tests.Projection;

public sealed class DynamicEnumExtractorTest
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly ISchemaProvider EmptySchema = new SchemaProviderBuilder().Build();
    // ── ParseEnumDefinitionFile ───────────────────────────────────────────────

    [Fact]
    public void ParseEnumDefinitionFile_ReadsChildElementNamesAsValues()
    {
        const string xml = """
                           <EnumDefinition>
                               <Offensive>0</Offensive>
                               <Defensive>1</Defensive>
                           </EnumDefinition>
                           """;

        var result = DynamicEnumExtractor.ParseEnumDefinitionFile(xml);

        Assert.Equal<IEnumerable<string>>(["Offensive", "Defensive"], result);
    }

    [Fact]
    public void ParseEnumDefinitionFile_SkipsXmlComments()
    {
        const string xml = """
                           <EnumDefinition>
                               <!-- this is a comment -->
                               <Fighter>0x00000001</Fighter>
                               <!-- another comment -->
                               <Bomber>0x00000002</Bomber>
                           </EnumDefinition>
                           """;

        var result = DynamicEnumExtractor.ParseEnumDefinitionFile(xml);

        Assert.Equal<IEnumerable<string>>(["Fighter", "Bomber"], result);
    }

    [Fact]
    public void ParseEnumDefinitionFile_EmptyDocument_ReturnsEmpty()
    {
        var result = DynamicEnumExtractor.ParseEnumDefinitionFile("<EnumDefinition></EnumDefinition>");

        Assert.Empty(result);
    }

    [Fact]
    public void ParseEnumDefinitionFile_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(DynamicEnumExtractor.ParseEnumDefinitionFile(null!));
        Assert.Empty(DynamicEnumExtractor.ParseEnumDefinitionFile(""));
    }

    [Fact]
    public void ParseEnumDefinitionFile_MalformedXml_ReturnsEmpty()
    {
        var result = DynamicEnumExtractor.ParseEnumDefinitionFile("<not valid xml <<<<");

        Assert.Empty(result);
    }

    // ── ParseElementTextWithLocations ────────────────────────────────────────

    [Fact]
    public void ParseElementTextWithLocations_ReturnsTokenAndPosition()
    {
        const string xml = "<GameConstants><Damage_Types>EXPLOSIVE ENERGY GRENADE</Damage_Types></GameConstants>";

        var result =
            DynamicEnumExtractor.ParseElementTextWithLocations(xml, "Damage_Types", "file:///gameconstants.xml");

        Assert.Equal(3, result.Count);
        Assert.Equal("EXPLOSIVE", result[0].Name);
        Assert.Equal("ENERGY", result[1].Name);
        Assert.Equal("GRENADE", result[2].Name);
        Assert.All(result, r => Assert.Equal("file:///gameconstants.xml", r.Origin.Uri));
        // All on line 0; ENERGY starts after "EXPLOSIVE " (10 chars after EXPLOSIVE start)
        Assert.True(result[1].Origin.Column > result[0].Origin.Column);
        Assert.True(result[2].Origin.Column > result[1].Origin.Column);
    }

    [Fact]
    public void ParseElementTextWithLocations_MultiLine_TracksLineBreaks()
    {
        const string xml = """
                           <GameConstants>
                               <Damage_Types>
                                   EXPLOSIVE
                                   ENERGY
                               </Damage_Types>
                           </GameConstants>
                           """;

        var result =
            DynamicEnumExtractor.ParseElementTextWithLocations(xml, "Damage_Types", "file:///gameconstants.xml");

        Assert.Equal(2, result.Count);
        Assert.Equal("EXPLOSIVE", result[0].Name);
        Assert.Equal("ENERGY", result[1].Name);
        // ENERGY is on a different line than EXPLOSIVE
        Assert.True(result[1].Origin.Line > result[0].Origin.Line);
    }

    [Fact]
    public void ParseElementTextWithLocations_StopsAtBoundaryComment()
    {
        const string xml = """
                           <GameConstants>
                             <Damage_Types>MOD_DMG
                           <!-- PLEASE add your new damage types ABOVE this point. -->
                           EXPLOSIVE ENERGY
                             </Damage_Types>
                           </GameConstants>
                           """;

        var result =
            DynamicEnumExtractor.ParseElementTextWithLocations(xml, "Damage_Types", "file:///gameconstants.xml");

        // Only MOD_DMG is before the boundary; EXPLOSIVE and ENERGY are hardcoded
        Assert.Equal("MOD_DMG", Assert.Single(result).Name);
    }

    [Fact]
    public void ParseElementTextWithLocations_MissingElement_ReturnsEmpty()
    {
        const string xml = "<GameConstants><Other_Types>SOMETHING</Other_Types></GameConstants>";

        Assert.Empty(DynamicEnumExtractor.ParseElementTextWithLocations(xml, "Damage_Types", "file:///x.xml"));
    }

    [Fact]
    public void ParseElementTextWithLocations_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(DynamicEnumExtractor.ParseElementTextWithLocations(null, "Damage_Types", "file:///x.xml"));
        Assert.Empty(DynamicEnumExtractor.ParseElementTextWithLocations("", "Damage_Types", "file:///x.xml"));
    }

    // ── ParseEnumDefinitionFileWithLocations ─────────────────────────────────

    [Fact]
    public void ParseEnumDefinitionFileWithLocations_ReturnsNameAndLineForEachElement()
    {
        const string xml = """
                           <EnumDefinition>
                               <GENERIC_TRACK>0</GENERIC_TRACK>
                               <INFANTRY_TERRAIN_MODIFIER>1</INFANTRY_TERRAIN_MODIFIER>
                           </EnumDefinition>
                           """;

        var result = DynamicEnumExtractor.ParseEnumDefinitionFileWithLocations(xml, "file:///surfacefxtriggertype.xml");

        Assert.Equal(2, result.Count);
        var first = result[0];
        Assert.Equal("GENERIC_TRACK", first.Name);
        Assert.Equal("file:///surfacefxtriggertype.xml", first.Origin.Uri);
        Assert.True(first.Origin.Line > 0); // must be past line 0 (the root element)
    }

    [Fact]
    public void ParseEnumDefinitionFileWithLocations_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(DynamicEnumExtractor.ParseEnumDefinitionFileWithLocations(null, "file:///x.xml"));
        Assert.Empty(DynamicEnumExtractor.ParseEnumDefinitionFileWithLocations("", "file:///x.xml"));
    }

    [Fact]
    public void ParseEnumDefinitionFileWithLocations_MalformedXml_ReturnsEmpty()
    {
        Assert.Empty(DynamicEnumExtractor.ParseEnumDefinitionFileWithLocations("<bad xml <<", "file:///x.xml"));
    }

    // ── Extract - schema with no DynamicXml enums ─────────────────────────────

    [Fact]
    public void Extract_EmptySchema_ReturnsEmpty()
    {
        var (dyn, hard) = DynamicEnumExtractor.Extract(EmptySchema, _ => null);

        Assert.Empty(dyn);
        Assert.Empty(hard);
    }

    [Fact]
    public void Extract_EnumWithNullSourceFile_IsIgnored()
    {
        var schema = WithEnums(new EnumDefinition
            { Name = "Orphan", Kind = EnumKind.DynamicXml, SourceFile = null, Values = [] });

        var (dyn, _) = DynamicEnumExtractor.Extract(schema, _ => null);

        Assert.Empty(dyn);
    }

    [Fact]
    public void Extract_SchemaFixedEnum_IsIgnored()
    {
        var schema = WithEnums(new EnumDefinition
            { Name = "Fixed", Kind = EnumKind.SchemaFixed, SourceFile = null, Values = [] });

        var (dyn, _) = DynamicEnumExtractor.Extract(schema, _ => null);

        Assert.Empty(dyn);
    }

    // ── Extract - dollar-format (GameConstants.xml$Element) ───────────────────

    [Fact]
    public void Extract_DollarFormat_ExtractsValuesFromGameConstantsXml()
    {
        var schema = WithEnums(DynEnum("DamageType", "data/xml/gameconstants.xml$Damage_Types"));
        const string xml = "<GameConstants><Damage_Types>EXPLOSIVE ENERGY GRENADE</Damage_Types></GameConstants>";

        var (dyn, _) = DynamicEnumExtractor.Extract(schema, GameConstantsReader(xml));

        Assert.Equal<IEnumerable<string>>(["EXPLOSIVE", "ENERGY", "GRENADE"], dyn["DamageType"]);
    }

    /// <summary>
    ///     The game's reader matches an element name in any case, so a registry's <c>$Damage_Types</c>
    ///     finds a <c>&lt;DAMAGE_TYPES&gt;</c> written in capitals, the way some shipped files write theirs.
    /// </summary>
    [Fact]
    public void Extract_DollarFormat_FindsTheElementWhateverItsCase()
    {
        var schema = WithEnums(DynEnum("DamageType", "data/xml/gameconstants.xml$Damage_Types"));
        const string xml = "<GameConstants><DAMAGE_TYPES>EXPLOSIVE ENERGY</DAMAGE_TYPES></GameConstants>";

        var (dyn, _) = DynamicEnumExtractor.Extract(schema, GameConstantsReader(xml));

        Assert.Equal<IEnumerable<string>>(["EXPLOSIVE", "ENERGY"], dyn["DamageType"]);
    }

    /// <summary>
    ///     A stray ampersand elsewhere in the file is a warning for its author and nothing more to the
    ///     game's reader (#198); a strict parser rejects the whole file and the enum loses its values.
    /// </summary>
    [Fact]
    public void Extract_DollarFormat_AStrayAmpersandElsewhere_DoesNotLoseTheFile()
    {
        var schema = WithEnums(DynEnum("DamageType", "data/xml/gameconstants.xml$Damage_Types"));
        const string xml =
            "<GameConstants><Title>Tom & Jerry</Title><Damage_Types>EXPLOSIVE</Damage_Types></GameConstants>";

        var (dyn, _) = DynamicEnumExtractor.Extract(schema, GameConstantsReader(xml));

        Assert.Equal<IEnumerable<string>>(["EXPLOSIVE"], dyn["DamageType"]);
    }

    [Fact]
    public void Extract_DollarFormat_WithBoundaryComment_PopulatesHardcoded()
    {
        var schema = WithEnums(DynEnum("DamageType", "data/xml/gameconstants.xml$Damage_Types"));
        const string xml = """
                           <GameConstants>
                             <Damage_Types>MOD_DMG
                           <!-- PLEASE add your new damage types ABOVE this point. -->
                           EXPLOSIVE ENERGY
                             </Damage_Types>
                           </GameConstants>
                           """;

        var (dyn, hard) = DynamicEnumExtractor.Extract(schema, GameConstantsReader(xml));

        Assert.Equal(3, dyn["DamageType"].Length);
        Assert.Equal<IEnumerable<string>>(["EXPLOSIVE", "ENERGY"], hard["DamageType"]);
    }

    [Fact]
    public void Extract_DollarFormat_NoBoundaryComment_HardcodedIsEmpty()
    {
        var schema = WithEnums(DynEnum("DamageType", "data/xml/gameconstants.xml$Damage_Types"));
        const string xml = "<GameConstants><Damage_Types>EXPLOSIVE ENERGY</Damage_Types></GameConstants>";

        var (_, hard) = DynamicEnumExtractor.Extract(schema, GameConstantsReader(xml));

        Assert.DoesNotContain("DamageType", hard.Keys);
    }

    [Fact]
    public void Extract_DollarFormat_FileReaderReturnsNull_YieldsEmpty()
    {
        var schema = WithEnums(DynEnum("DamageType", "data/xml/gameconstants.xml$Damage_Types"));

        var (dyn, hard) = DynamicEnumExtractor.Extract(schema, _ => null);

        Assert.Empty(dyn);
        Assert.Empty(hard);
    }

    [Fact]
    public void Extract_TwoDollarEnumsSameFile_FileReadOnlyOnce()
    {
        var schema = WithEnums(
            DynEnum("DamageType", "data/xml/gameconstants.xml$Damage_Types"),
            DynEnum("ArmorType", "data/xml/gameconstants.xml$Armor_Types"));
        const string xml = """
                           <GameConstants>
                             <Damage_Types>EXPLOSIVE</Damage_Types>
                             <Armor_Types>ARMOR_INFANTRY</Armor_Types>
                           </GameConstants>
                           """;

        var readCount = 0;
        var (dyn, _) = DynamicEnumExtractor.Extract(schema, path =>
        {
            readCount++;
            return path.Contains("gameconstants", StringComparison.OrdinalIgnoreCase) ? xml : null;
        });

        Assert.Equal(1, readCount);
        Assert.Equal<IEnumerable<string>>(["EXPLOSIVE"], dyn["DamageType"]);
        Assert.Equal<IEnumerable<string>>(["ARMOR_INFANTRY"], dyn["ArmorType"]);
    }

    // ── Extract - file-format (data/xml/enum/*.xml) ───────────────────────────

    [Fact]
    public void Extract_FileFormat_ReadsFromFileReader()
    {
        var schema = WithEnums(DynEnum("AIGoalCategoryType", "data/xml/enum/aigoalcategorytype.xml"));
        const string enumXml = "<EnumDefinition><Offensive>0</Offensive><Defensive>1</Defensive></EnumDefinition>";

        var (dyn, hard) = DynamicEnumExtractor.Extract(schema, path =>
            path == "data/xml/enum/aigoalcategorytype.xml" ? enumXml : null);

        Assert.Equal<IEnumerable<string>>(["Offensive", "Defensive"], dyn["AIGoalCategoryType"]);
        Assert.Empty(hard);
    }

    [Fact]
    public void Extract_FileFormat_FileReaderReturnsNull_SkipsEnum()
    {
        var schema = WithEnums(DynEnum("AIGoalCategoryType", "data/xml/enum/aigoalcategorytype.xml"));

        var (dyn, _) = DynamicEnumExtractor.Extract(schema, _ => null);

        Assert.Empty(dyn);
    }

    // ── Extract - mixed formats ────────────────────────────────────────────────

    [Fact]
    public void Extract_BothFormats_AllEnumsExtracted()
    {
        var schema = WithEnums(
            DynEnum("DamageType", "data/xml/gameconstants.xml$Damage_Types"),
            DynEnum("AIGoalCategoryType", "data/xml/enum/aigoalcategorytype.xml"));
        const string gameConstantsXml =
            "<GameConstants><Damage_Types>EXPLOSIVE</Damage_Types></GameConstants>";
        const string enumXml = "<EnumDefinition><Offensive>0</Offensive></EnumDefinition>";

        var (dyn, _) = DynamicEnumExtractor.Extract(schema, path =>
            path.Contains("gameconstants", StringComparison.OrdinalIgnoreCase)
                ? gameConstantsXml
                : path == "data/xml/enum/aigoalcategorytype.xml"
                    ? enumXml
                    : null);

        Assert.Equal<IEnumerable<string>>(["EXPLOSIVE"], dyn["DamageType"]);
        Assert.Equal<IEnumerable<string>>(["Offensive"], dyn["AIGoalCategoryType"]);
    }

    private static ISchemaProvider WithEnums(params EnumDefinition[] enums)
    {
        var builder = new SchemaProviderBuilder();
        foreach (var definition in enums)
            builder.WithEnum(definition);
        return builder.Build();
    }

    private static EnumDefinition DynEnum(string name, string sourceFile)
    {
        return new EnumDefinition
        {
            Name = name,
            Kind = EnumKind.DynamicXml,
            SourceFile = sourceFile,
            Values = []
        };
    }

    private static Func<string, string?> GameConstantsReader(string content)
    {
        return path => path.Contains("gameconstants", StringComparison.OrdinalIgnoreCase) ? content : null;
    }
}