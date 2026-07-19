// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Tests;

/// <summary>
///     Builds a real <see cref="XmlParseCache" /> over a test workspace host - the standard way
///     for handler tests to satisfy the <c>IXmlParseCache</c> dependency.
/// </summary>
internal static class TestParseCache
{
    public static XmlParseCache For(IGameWorkspaceHost host, IFileHelper? fileHelper = null, int capacity = 16)
    {
        return new XmlParseCache(
            new DocumentTextSource(host, fileHelper ?? new FileHelper(new MockFileSystem()),
                NullLogger<DocumentTextSource>.Instance),
            capacity);
    }
}