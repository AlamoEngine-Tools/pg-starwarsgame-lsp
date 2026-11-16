// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Core.Tests.Diagnostics;

/// <summary>
///     A diagnostic's message is a string or markup since LSP 3.18. Whatever reads it as text reads
///     the words, never the type's own formatting.
/// </summary>
public sealed class DiagnosticMessageTextTest
{
    [Fact]
    public void Text_OfAPlainMessage_IsTheMessage()
    {
        var diagnostic = new Diagnostic { Message = "'X-Wing' is not used" };

        Assert.Equal("'X-Wing' is not used", diagnostic.Message.Text());
    }

    // ToString() of a markup message prefixes its kind - "[Markdown] ..." - which is not the text.
    [Fact]
    public void Text_OfAMarkupMessage_IsItsSourceText()
    {
        var diagnostic = new Diagnostic
        {
            Message = new MarkupContent { Kind = MarkupKind.Markdown, Value = "**'X-Wing'** is not used" }
        };

        Assert.Equal("**'X-Wing'** is not used", diagnostic.Message.Text());
    }

    [Fact]
    public void Text_OfNoMessage_IsEmpty()
    {
        StringOrMarkupContent? none = null;

        Assert.Equal("", none.Text());
    }
}