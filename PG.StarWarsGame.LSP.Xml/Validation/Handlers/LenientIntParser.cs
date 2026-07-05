// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Integer parsing with the game's tolerance: a float-formatted number in an integer slot is
///     accepted (the engine's atoi-style parsing truncates it) but flagged via
///     <paramref name="wasFloat" /> so callers emit a Warning with the truncated value as the
///     suggested fix instead of a hard Error. Keeps every int-slot handler on one policy.
/// </summary>
internal static class LenientIntParser
{
    public static bool TryParse(string token, out int value, out bool wasFloat)
    {
        if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            wasFloat = false;
            return true;
        }

        if (LenientFloatParser.TryParse(token, out var floatVal)
            && floatVal is >= int.MinValue and <= int.MaxValue)
        {
            value = (int)floatVal;
            wasFloat = true;
            return true;
        }

        value = 0;
        wasFloat = false;
        return false;
    }
}
