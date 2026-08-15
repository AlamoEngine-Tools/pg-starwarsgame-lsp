// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     The textures a model names INSIDE itself, which no XML tag mentions.
/// </summary>
/// <remarks>
///     Every other asset-existence handler validates a reference written in the document. A model's
///     own textures are written in the binary, so until this handler the only thing that ever asked
///     whether they resolved was the 3D preview, at draw time, in the webview - which meant a model
///     with a missing skin reported nothing at all unless somebody happened to open it.
/// </remarks>
public sealed class ModelTextureExistenceHandlerTest
{
    private static readonly ModelTextureExistenceHandler Sut = new();

    /// <summary>Stands in for the reader that parses an .alo; the Xml layer cannot open one.</summary>
    private sealed class FakeModelTextures(params string[] textures) : IModelTextureIndex
    {
        public string? Asked { get; private set; }

        public IReadOnlyList<string> TexturesOf(string modelReference)
        {
            Asked = modelReference;
            return textures;
        }
    }

    private static XmlTagDefinition Tag(ReferenceKind kind = ReferenceKind.ModelFile)
    {
        return XmlHandlerTestFixtures.MakeTag("Model_Name", XmlValueType.NameReference,
            referenceKind: kind);
    }

    private static DiagnosticsContext CtxWith(IModelTextureIndex? textures, params string[] paths)
    {
        var index = GameIndex.Empty with { AssetFiles = new MergedAssetFileIndex(paths) };
        return new DiagnosticsContext(new EmptySchemaProvider(), index, "file:///test.xml", "en",
            ModelTextures: textures);
    }

    [Fact]
    public void EveryTexturePresent_EmitsNothing()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(), "ev_ship.alo");
        var ctx = CtxWith(new FakeModelTextures("hull.tga", "glow.tga"),
            "data/art/models/ev_ship.alo", "data/art/textures/hull.tga",
            "data/art/textures/glow.tga");

        Assert.Empty(Sut.Handle(fact, ctx));
    }

    [Fact]
    public void MissingTexture_WarnsAndNamesBothTheModelAndTheTexture()
    {
        // The tag names the model, so the model alone is not enough to act on - and the texture
        // alone does not say where it is referenced from.
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(), "ev_ship.alo");
        var ctx = CtxWith(new FakeModelTextures("hull.tga", "gone.tga"),
            "data/art/models/ev_ship.alo", "data/art/textures/hull.tga");

        var d = Assert.Single(Sut.Handle(fact, ctx));

        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("gone.tga", d.Message);
        Assert.Contains("ev_ship.alo", d.Message);
    }

    [Fact]
    public void MissingTextures_WarnOncePerTexture()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(), "ev_ship.alo");
        var ctx = CtxWith(new FakeModelTextures("one.tga", "two.tga"),
            "data/art/models/ev_ship.alo");

        Assert.Equal(2, Sut.Handle(fact, ctx).Count());
    }

    [Fact]
    public void SameTextureTwice_WarnsOnce()
    {
        // A model binds one skin across many sub-meshes. Reporting it per sub-mesh would bury the
        // one thing the reader has to fix under a dozen copies of itself.
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(), "ev_ship.alo");
        var ctx = CtxWith(new FakeModelTextures("gone.tga", "gone.tga", "GONE.TGA"),
            "data/art/models/ev_ship.alo");

        Assert.Single(Sut.Handle(fact, ctx));
    }

    [Fact]
    public void TgaReferenceSatisfiedByDds_EmitsNothing()
    {
        // The engine resolves a texture by basename across both formats, exactly as
        // TextureFileExistenceHandler already allows for a reference written in the XML.
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(), "ev_ship.alo");
        var ctx = CtxWith(new FakeModelTextures("hull.tga"),
            "data/art/models/ev_ship.alo", "data/art/textures/hull.dds");

        Assert.Empty(Sut.Handle(fact, ctx));
    }

    [Fact]
    public void NotAModelReference_EmitsNothing()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(ReferenceKind.TextureFile), "hull.tga");
        var ctx = CtxWith(new FakeModelTextures("gone.tga"), "data/art/textures/hull.tga");

        Assert.Empty(Sut.Handle(fact, ctx));
    }

    [Fact]
    public void NoReader_EmitsNothing()
    {
        // The Xml layer cannot open an .alo on its own, so a host that supplies no reader gets no
        // diagnostics rather than a wrong one - the same shape as the icon repack provider.
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(), "ev_ship.alo");

        Assert.Empty(Sut.Handle(fact, CtxWith(null, "data/art/models/ev_ship.alo")));
    }

    [Fact]
    public void AsksAboutTheModelTheTagNames()
    {
        var reader = new FakeModelTextures();
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(), "ev_ship.alo");

        _ = Sut.Handle(fact, CtxWith(reader, "data/art/models/ev_ship.alo")).ToList();

        Assert.Equal("ev_ship.alo", reader.Asked);
    }

    [Fact]
    public void ModelThatDoesNotResolve_EmitsNothing()
    {
        // ModelFileExistence already reports that, and saying "this model is missing" followed by
        // "and so are all of its textures" is noise on top of the one useful line.
        var reader = new FakeModelTextures("gone.tga");
        var fact = XmlHandlerTestFixtures.MakeFact(Tag(), "absent.alo");

        Assert.Empty(Sut.Handle(fact, CtxWith(reader, "data/art/textures/hull.tga")));
    }
}
