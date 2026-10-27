// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Text;

namespace PG.StarWarsGame.LSP.Core.Tests.Text;

/// <summary>
///     The one line-ending rule every writer follows (#313): a text with any CRLF in it is written
///     with CRLF, anything else with LF. Measured on the five detectors it replaced, which all agreed.
/// </summary>
public sealed class LineEndingsTest
{
    [Fact]
    public void Detect_LfOnly_IsLf()
    {
        Assert.Equal("\n", LineEndings.Detect("a\nb\nc\n"));
    }

    [Fact]
    public void Detect_CrLfOnly_IsCrLf()
    {
        Assert.Equal("\r\n", LineEndings.Detect("a\r\nb\r\nc\r\n"));
    }

    /// <summary>A mixed file is written with CRLF wherever the CRLF sits - the first break does not decide.</summary>
    [Fact]
    public void Detect_Mixed_IsCrLf()
    {
        Assert.Equal("\r\n", LineEndings.Detect("a\nb\r\nc\n"));
        Assert.Equal("\r\n", LineEndings.Detect("a\r\nb\nc\n"));
    }

    [Fact]
    public void Detect_NoLineBreak_IsLf()
    {
        Assert.Equal("\n", LineEndings.Detect("one line"));
        Assert.Equal("\n", LineEndings.Detect(""));
    }

    /// <summary>A lone CR is no line ending the engine's files use; it does not make the text CRLF.</summary>
    [Fact]
    public void Detect_LoneCr_IsLf()
    {
        Assert.Equal("\n", LineEndings.Detect("a\rb\nc"));
    }
}