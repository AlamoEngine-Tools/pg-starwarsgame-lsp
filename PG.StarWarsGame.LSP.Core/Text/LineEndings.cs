// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Text;

/// <summary>
///     The line ending a writer continues an author's file with. One rule for every writer (#313):
///     a text with any CRLF in it is CRLF, anything else - LF only, a lone CR, no break at all - is LF.
/// </summary>
public static class LineEndings
{
    public static string Detect(string text)
    {
        return text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }
}