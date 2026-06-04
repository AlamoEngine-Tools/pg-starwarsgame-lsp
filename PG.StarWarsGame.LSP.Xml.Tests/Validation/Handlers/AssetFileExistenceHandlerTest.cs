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

    private static XmlTagDefinition Tag(ReferenceKind kind) =>
        XmlHandlerTestFixtures.MakeTag("Texture", XmlValueType.NameReference, referenceKind: kind);

    private static DiagnosticsContext CtxWith(params string[] paths)
    {
        var index = GameIndex.Empty with { AssetFiles = new MergedAssetFileIndex(paths) };
        return new DiagnosticsContext(new EmptySchemaProvider(), index, "file:///test.xml", "en");
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
    public void Map_Present_EmitsNothing()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(ReferenceKind.MapFile), "skirmish.ted");
        var ctx = CtxWith("data/maps/skirmish.ted");

        Assert.Empty(MapSut.Handle(fact, ctx));
    }

    [Fact]
    public void EmptyCatalog_Absent_EmitsWarning()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(ReferenceKind.TextureFile), "foo.tga");

        Assert.Single(TextureSut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx));
    }
}
