// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;
using Xunit;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class AudioFileFormatHandlerTest
{
    private static readonly AudioFileFormatHandler Sut = new();

    private static readonly XmlTagDefinition AudioTag =
        XmlHandlerTestFixtures.MakeTag("Samples", XmlValueType.NameReferenceList,
            referenceKind: ReferenceKind.AudioFile);

    [Theory]
    [InlineData("Audio/hit.wav")]
    [InlineData("Audio/hit.WAV")]
    [InlineData("Audio/hit.mp3")]
    [InlineData("Audio/hit.MP3")]
    public void Single_valid_audio_extension_returns_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(AudioTag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Multiple_valid_tokens_return_no_diagnostics()
    {
        var results = Sut.Handle(
            XmlHandlerTestFixtures.MakeFact(AudioTag, "hit.wav\n  boom.wav\n  swoosh.mp3"),
            XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("hit.ogg")]
    [InlineData("hit.tga")]
    [InlineData("hit.alo")]
    [InlineData("hit")]
    public void Single_invalid_token_returns_one_error(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(AudioTag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public void One_invalid_token_in_list_returns_one_error()
    {
        var results = Sut.Handle(
            XmlHandlerTestFixtures.MakeFact(AudioTag, "hit.wav\n  bad.ogg\n  boom.wav"),
            XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Contains("bad.ogg", d.Message);
    }

    [Fact]
    public void Two_invalid_tokens_return_two_errors()
    {
        var results = Sut.Handle(
            XmlHandlerTestFixtures.MakeFact(AudioTag, "bad.ogg\n  hit.wav\n  also_bad.tga"),
            XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(XmlDiagnosticSeverity.Error, r.Severity));
    }

    [Fact]
    public void Non_audio_tag_returns_no_diagnostics()
    {
        var otherTag = XmlHandlerTestFixtures.MakeTag("Speed", XmlValueType.Float);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(otherTag, "hit.ogg"), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }
}
