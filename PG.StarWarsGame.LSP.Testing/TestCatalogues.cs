// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Messages;

namespace PG.StarWarsGame.LSP.Testing;

/// <summary>The catalogues a test hands to code that formats a text.</summary>
public static class TestCatalogues
{
    /// <summary>The server's own texts in English - what a test asserting on wording reads.</summary>
    public static IMessageCatalog English { get; } = ResxMessageCatalog.Server("en");
}