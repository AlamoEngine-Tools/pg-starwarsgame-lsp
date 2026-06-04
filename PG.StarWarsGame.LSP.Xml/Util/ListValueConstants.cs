// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;

namespace PG.StarWarsGame.LSP.Xml.Util;

internal static class ListValueConstants
{
    public const char SeparatorSpace = ' ';

    public const char SeparatorPipe = '|';

    public const char SeparatorComma = ',';

    public static char[] GetListSeparators()
    {
        return [SeparatorSpace, SeparatorPipe, SeparatorComma];
    }

    public static string PrepareValueForSplit(string value)
    {
        return Regex.Replace(value, @"\s+", " ").Trim();
    }
}