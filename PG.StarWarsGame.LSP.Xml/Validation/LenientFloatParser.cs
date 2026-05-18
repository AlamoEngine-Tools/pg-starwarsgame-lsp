// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;

namespace PG.StarWarsGame.LSP.Xml.Validation;

internal static class LenientFloatParser
{
    internal static bool TryParse(string s, out float result)
    {
        var trimmed = s.Length > 0 && (s[^1] == 'f' || s[^1] == 'F') ? s[..^1] : s;
        return float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }
}