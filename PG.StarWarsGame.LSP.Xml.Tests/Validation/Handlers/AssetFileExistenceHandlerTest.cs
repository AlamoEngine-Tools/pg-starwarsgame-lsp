// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class AssetFileExistenceHandlerTest
{
    private static readonly TextureFileExistenceHandler TextureSut = new();
    private static readonly ModelFileExistenceHandler ModelSut = new();
    private static readonly AudioFileExistenceHandler AudioSut = new();
    private static readonly MapFileExistenceHandler MapSut = new();

    private static XmlTagDefinition Tag(ReferenceKind kind)
    {
        return XmlHandlerTestFixtures.MakeTag("Texture", XmlValueType.NameReference, referenceKind: kind);
    }

    private static DiagnosticsContext CtxWith(params string[] paths)
    {
        var index = GameIndex.Empty with { AssetFiles = new MergedAssetFileIndex(paths) };
        return new DiagnosticsContext(new EmptySchemaProvider(), index, "file:///test.xml", "en");
    }

    private static DiagnosticsContext CtxWithIcons(IIconNameIndex icons, params string[] paths)
    {
        var index = GameIndex.Empty with { AssetFiles = new MergedAssetFileIndex(paths) };
        return new DiagnosticsContext(new EmptySchemaProvider(), index, "file:///test.xml", "en",
            IconNames: icons);
    }

    // ── texture ──────────────────────────────────────────────────────────────

    [Fact]
    public void Texture_PresentFullPath_EmitsNothing()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(ReferenceKind.TextureFile), "data/art/textures/foo.tga");
        var ctx = CtxWith("data/art/textures/foo.tga");

        Assert.Empty(TextureSut.Handle(fact, ctx));
    }

    [Fact]
    public void Texture_PresentBareFilename_EmitsNothing()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(ReferenceKind.TextureFile), "foo.tga");
        var ctx = CtxWith("data/art/textures/foo.tga");

        Assert.Empty(TextureSut.Handle(fact, ctx));
    }

    [Fact]
    public void Texture_Absent_EmitsWarning()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(ReferenceKind.TextureFile), "missing.tga");
        var ctx = CtxWith("data/art/textures/foo.tga");

        var d = Assert.Single(TextureSut.Handle(fact, ctx));
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("missing.tga", d.Message);
    }

    [Fact]
    public void Texture_CaseInsensitiveLookup_EmitsNothing()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(ReferenceKind.TextureFile), "FOO.TGA");
        var ctx = CtxWith("data/art/textures/foo.tga");

        Assert.Empty(TextureSut.Handle(fact, ctx));
    }

    [Fact]
    public void Texture_EmptyValue_EmitsNothing()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(ReferenceKind.TextureFile), "   ");
        var ctx = CtxWith("data/art/textures/foo.tga");

        Assert.Empty(TextureSut.Handle(fact, ctx));
    }

    [Fact]
    public void Texture_WrongReferenceKind_EmitsNothing()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(ReferenceKind.ModelFile), "missing.tga");
        var ctx = CtxWith("data/art/textures/foo.tga");

        Assert.Empty(TextureSut.Handle(fact, ctx));
    }

    // ── TGA/DDS interchangeability ───────────────────────────────────────────
    // The engine treats TGA and DDS as one texture: a .tga reference is satisfied by the .dds
    // (and vice versa); TGA wins when both exist. Only both-missing is a real problem.

    [Fact]
    public void Texture_TgaReferenced_OnlyDdsPresent_EmitsNothing()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(ReferenceKind.TextureFile), "foo.tga");
        var ctx = CtxWith("data/art/textures/foo.dds");

        Assert.Empty(TextureSut.Handle(fact, ctx));
    }

    [Fact]
    public void Texture_DdsReferenced_OnlyTgaPresent_EmitsNothing()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(ReferenceKind.TextureFile), "foo.dds");
        var ctx = CtxWith("data/art/textures/foo.tga");

        Assert.Empty(TextureSut.Handle(fact, ctx));
    }

    [Fact]
    public void Texture_TgaReferencedFullPath_OnlyDdsPresent_EmitsNothing()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(ReferenceKind.TextureFile),
            "data/art/textures/foo.tga");
        var ctx = CtxWith("data/art/textures/foo.dds");

        Assert.Empty(TextureSut.Handle(fact, ctx));
    }

    [Fact]
    public void Texture_NeitherFormatPresent_WarnsAndMentionsTheAlternate()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(ReferenceKind.TextureFile), "missing.tga");
        var ctx = CtxWith("data/art/textures/foo.tga");

        var d = Assert.Single(TextureSut.Handle(fact, ctx));
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("missing.tga", d.Message);
        Assert.Contains("missing.dds", d.Message);
    }

    [Fact]
    public void Model_Absent_NoCrossExtensionFallback()
    {
        // Interchangeability is a TEXTURE rule; other asset types keep exact-extension matching.
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(ReferenceKind.ModelFile), "missing.alo");
        var ctx = CtxWith("data/art/models/missing.dds");

        Assert.Single(ModelSut.Handle(fact, ctx));
    }

    // ── textures packed into a mega texture ──────────────────────────────────────

    // The bug this fixes: a GUI texture that ships INSIDE a mega texture is not a file anywhere, so
    // the file lookup could only ever fail. The preview drew the icon perfectly well while the
    // editor warned that it did not exist.
    [Fact]
    public void Texture_AbsentAsFileButPackedIntoMegaTexture_EmitsNothing()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(
            Tag(ReferenceKind.TextureFile), "i_button_EV_ExecutorStarDestroyer.tga");
        var ctx = CtxWithIcons(
            new FakeIconNames("I_BUTTON_EV_EXECUTORSTARDESTROYER"), "data/art/textures/foo.tga");

        Assert.Empty(TextureSut.Handle(fact, ctx));
    }

    // A .mtd records its entries uppercase whatever the packer was fed, and without the extension
    // the XML writes. Both halves have to be forgiven or the fix helps nobody.
    [Fact]
    public void Texture_PackedUnderDifferentCasing_EmitsNothing()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(ReferenceKind.TextureFile), "I_Button_Foo.TGA");
        var ctx = CtxWithIcons(new FakeIconNames("i_button_foo"));

        Assert.Empty(TextureSut.Handle(fact, ctx));
    }

    [Fact]
    public void Texture_AbsentFromBothFilesAndMegaTexture_StillEmitsWarning()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(ReferenceKind.TextureFile), "missing.tga");
        var ctx = CtxWithIcons(new FakeIconNames("I_BUTTON_SOMETHING_ELSE"));

        var d = Assert.Single(TextureSut.Handle(fact, ctx));
        Assert.Contains("missing.tga", d.Message);
    }

    // No host supplies one? Then nothing changes and the file lookup still decides on its own.
    [Fact]
    public void Texture_NoMegaTextureSupplied_StillEmitsWarning()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(ReferenceKind.TextureFile), "missing.tga");

        Assert.Single(TextureSut.Handle(fact, CtxWith("data/art/textures/foo.tga")));
    }

    // Scoping. A mega texture holds GUI art and nothing else, so a model, a sound or a map must not
    // be excused by one - that would turn a real missing-asset warning into silence.
    [Fact]
    public void Model_NamedInAMegaTexture_StillEmitsWarning()
    {
        var tag = XmlHandlerTestFixtures.MakeTag(
            "Model", XmlValueType.NameReference, referenceKind: ReferenceKind.ModelFile);
        var fact = XmlHandlerTestFixtures.MakeFact(tag, "ev_executor.alo");
        var ctx = CtxWithIcons(new FakeIconNames("ev_executor"));

        Assert.Single(ModelSut.Handle(fact, ctx));
    }

    // ── model / audio / map gating ───────────────────────────────────────────

    [Fact]
    public void Model_Present_EmitsNothing()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(ReferenceKind.ModelFile), "bar.alo");
        var ctx = CtxWith("data/art/models/bar.alo");

        Assert.Empty(ModelSut.Handle(fact, ctx));
    }

    [Fact]
    public void Audio_Absent_EmitsWarning()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(ReferenceKind.AudioFile), "missing.wav");
        var ctx = CtxWith("data/audio/hit.wav");

        Assert.Single(AudioSut.Handle(fact, ctx));
    }

    [Fact]
    public void SingleValuedTag_NameContainingASpace_IsOneName()
    {
        // #124. Asset values were split on space, pipe and comma whatever the tag's type, so a model
        // called "CIS_Vazus Mandrake.alo" was looked up as two files and reported missing. Spaces in
        // asset names are not a mod-only oddity: Mt_commandbar.mtd ships
        // "I_BUTTON_EV_MDU_GRENADE MORTAR.TGA", and four vanilla Icon_Name values carry one.
        var tag = XmlHandlerTestFixtures.MakeTag("Land_Model_Name", XmlValueType.NameReference,
            referenceKind: ReferenceKind.ModelFile);
        var fact = XmlHandlerTestFixtures.MakeFact(tag, "CIS_Vazus Mandrake.alo");
        var ctx = CtxWith("data/art/models/cis_vazus mandrake.alo");

        Assert.Empty(ModelSut.Handle(fact, ctx));
    }

    [Fact]
    public void ListValuedTag_StillSplitsOnSpace()
    {
        // The control: a list tag means several files, and 16 asset tags are list-typed - audio
        // Samples alone appears 2868 times with space-separated names.
        var tag = XmlHandlerTestFixtures.MakeTag("Samples", XmlValueType.NameReferenceList,
            referenceKind: ReferenceKind.AudioFile);
        var fact = XmlHandlerTestFixtures.MakeFact(tag, "one.wav missing.wav");
        var ctx = CtxWith("data/audio/one.wav");

        var d = Assert.Single(AudioSut.Handle(fact, ctx));
        Assert.Contains("missing.wav", d.Message);
    }

    /// <summary>
    ///     Each unresolved name in a list is anchored on ITSELF, not on the whole value.
    /// </summary>
    /// <remarks>
    ///     Measured on the corpus: <c>commandbarcomponents.xml</c> carries
    ///     <c>&lt;Icon_Alternate_Texture_Name&gt;</c> lists of 27 names on one line, and 28 warnings
    ///     were landing on the same 759-character range. The author saw a stack of identical
    ///     squiggles over the whole value with nothing to say which name was the bad one - a
    ///     diagnostic that is right and unusable. 42 ranges in that one file carried more than one.
    /// </remarks>
    [Fact]
    public void ListValuedTag_AnchorsEachNameOnItself()
    {
        var tag = XmlHandlerTestFixtures.MakeTag("Samples", XmlValueType.NameReferenceList,
            referenceKind: ReferenceKind.AudioFile);
        const string raw = "one.wav missing.wav other.wav gone.wav";
        var fact = XmlHandlerTestFixtures.MakeFact(tag, raw);
        var ctx = CtxWith("data/audio/one.wav", "data/audio/other.wav");

        var results = AudioSut.Handle(fact, ctx).ToList();

        Assert.Equal(2, results.Count);
        foreach (var d in results)
        {
            var name = d.Message.Split('\'')[1];
            Assert.Equal(raw.IndexOf(name, StringComparison.Ordinal), d.OverrideColumn);
            Assert.Equal(name.Length, d.OverrideLength);
            Assert.Equal(0, d.OverrideLine);
        }
    }

    /// <summary>
    ///     A list that spans lines keeps each name on the line it is written on.
    /// </summary>
    [Fact]
    public void ListValuedTag_AnchorsAcrossLines()
    {
        var tag = XmlHandlerTestFixtures.MakeTag("Samples", XmlValueType.NameReferenceList,
            referenceKind: ReferenceKind.AudioFile);
        var fact = XmlHandlerTestFixtures.MakeFact(tag, "one.wav\n    missing.wav");
        var ctx = CtxWith("data/audio/one.wav");

        var d = Assert.Single(AudioSut.Handle(fact, ctx));
        Assert.Equal(1, d.OverrideLine);
        Assert.Equal(4, d.OverrideColumn);
        Assert.Equal("missing.wav".Length, d.OverrideLength);
    }

    /// <summary>
    ///     <c>NOT_USED</c> fills a slot, it does not name a file.
    /// </summary>
    /// <remarks>
    ///     <c>&lt;Icon_Alternate_Texture_Name&gt;</c> is a list indexed BY ABILITY SLOT, and vanilla
    ///     writes the literal <c>NOT_USED</c> in slot 0 to say that slot carries no icon - 23
    ///     occurrences in <c>commandbarcomponents.xml</c>, in both corpora. The engine has no such
    ///     string in it and simply fails to load the texture, which is exactly what the author
    ///     intended, so reporting a missing file says the opposite of what happened.
    /// </remarks>
    [Fact]
    public void ListValuedTag_NotUsedSlotMarker_IsNotAReference()
    {
        var tag = XmlHandlerTestFixtures.MakeTag("Icon_Alternate_Texture_Name",
            XmlValueType.NameReferenceList, referenceKind: ReferenceKind.TextureFile);
        var fact = XmlHandlerTestFixtures.MakeFact(tag, "NOT_USED  present.tga");
        var ctx = CtxWith("data/art/textures/present.tga");

        Assert.Empty(TextureSut.Handle(fact, ctx));
    }

    /// <summary>
    ///     The marker is a LIST slot filler. Alone in a single-valued tag it is a real value, and a
    ///     texture called that really is missing.
    /// </summary>
    [Fact]
    public void SingleValuedTag_NotUsed_IsStillReported()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(ReferenceKind.TextureFile), "NOT_USED");
        var ctx = CtxWith("data/art/textures/foo.tga");

        Assert.Single(TextureSut.Handle(fact, ctx));
    }

    [Fact]
    public void Map_Present_EmitsNothing()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(ReferenceKind.MapFile), "skirmish.ted");
        var ctx = CtxWith("data/maps/skirmish.ted");

        Assert.Empty(MapSut.Handle(fact, ctx));
    }

    [Fact]
    public void Map_Absent_IsAnError_NotAWarning()
    {
        // #132. A missing map is not a degraded battle, it is no battle: measured in the 2018 build,
        // the engine retries with the resolved map path and a .ted extension, and when the file
        // still will not open it asserts and returns false. The hardcoded _Desert_L5_01.ted /
        // _Space_Temperate1.ted defaults sit on the EMPTY-name path, not on this one, so nothing
        // stands in for a map that was named and is not there.
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(ReferenceKind.MapFile), "missing.ted");
        var ctx = CtxWith("data/maps/skirmish.ted");

        var d = Assert.Single(MapSut.Handle(fact, ctx));
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public void Model_Absent_StaysAWarning()
    {
        // The control for the map change: a missing model degrades what you see, it does not stop
        // the game loading, so the severity split has to be per asset kind rather than global.
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(ReferenceKind.ModelFile), "missing.alo");
        var ctx = CtxWith("data/art/models/x.alo");

        var d = Assert.Single(ModelSut.Handle(fact, ctx));
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
    }

    [Fact]
    public void EmptyCatalog_Absent_EmitsWarning()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(ReferenceKind.TextureFile), "foo.tga");

        Assert.Single(TextureSut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx));
    }

    /// <summary>A stand-in mega texture holding <paramref name="names" />, as a .mtd records them.</summary>
    private sealed class FakeIconNames(params string[] names) : IIconNameIndex
    {
        private readonly HashSet<string> _names = new(names, StringComparer.OrdinalIgnoreCase);

        public bool Contains(string reference)
        {
            var dot = reference.LastIndexOf('.');
            return _names.Contains(dot > 0 ? reference[..dot] : reference);
        }
    }
}