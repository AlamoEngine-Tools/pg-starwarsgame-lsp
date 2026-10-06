// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using HtmlAgilityPack;

namespace PG.StarWarsGame.LSP.Xml.Util;

/// <summary>
///     HtmlAgilityPack parses some tag names by HTML rules out of a process-wide table: <c>base</c>,
///     <c>img</c>, <c>meta</c> and others are void (their content becomes a sibling), <c>title</c>,
///     <c>script</c>, <c>style</c> and <c>textarea</c> are raw text (their children are not parsed).
///     Game XML uses such names as ordinary elements - vanilla AI hint sets carry
///     <c>&lt;Base&gt;0.0&lt;/Base&gt;</c>, which read as empty - and every HtmlAgilityPack parse in
///     this process is game XML, so the table is emptied once, when this assembly loads.
/// </summary>
internal static class GameXmlParsingInitializer
{
#pragma warning disable CA2255 // a module initializer is the point: it must run before any parse
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void ParseEveryElementAlike()
    {
        HtmlNode.ElementsFlags.Clear();
    }
}
