// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Tests.Symbols;

public sealed class FileOriginLspExtensionsTest
{
    [Fact]
    public void ToLspLocation_UsesOriginUri()
    {
        var origin = new FileOrigin("file:///a/units.xml", 5, 10);
        var location = origin.ToLspLocation();
        Assert.Equal("file:///a/units.xml", location.Uri.ToString());
    }

    [Fact]
    public void ToLspLocation_ZeroWidthRangeAtLineAndColumn()
    {
        var origin = new FileOrigin("file:///a/units.xml", 5, 10);
        var location = origin.ToLspLocation();

        Assert.Equal(new Position(5, 10), location.Range.Start);
        Assert.Equal(new Position(5, 10), location.Range.End);
    }

    [Fact]
    public void ToLspLocation_NullColumn_DefaultsToZero()
    {
        var origin = new FileOrigin("file:///a/units.xml", 5, null);
        var location = origin.ToLspLocation();

        Assert.Equal(new Position(5, 0), location.Range.Start);
        Assert.Equal(new Position(5, 0), location.Range.End);
    }
}
