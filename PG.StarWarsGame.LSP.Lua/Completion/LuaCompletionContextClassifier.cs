// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Completion;

internal static class LuaCompletionContextClassifier
{
    // The one context this server completes: an argument, inside its string or before any quote.
    // Everything else is the analyzer's.
    public static ArgumentContext? Classify(string text, int line, int character)
    {
        var textLines = text.Split('\n');
        if (line >= textLines.Length)
            return null;

        var lineText = textLines[line].TrimEnd('\r');
        var col = Math.Min(character, lineText.Length);
        return TryBareArgContext(lineText, col) ?? TryStringArgContext(lineText, col);
    }

    // The cursor at the start of an argument, or in a name typed there: Find_Player(| or
    // Find_Player(Em|. Not in a declaration's parameter list.
    private static ArgumentContext? TryBareArgContext(string line, int col)
    {
        var i = col - 1;
        while (i >= 0 && IsNameChar(line[i])) i--;
        while (i >= 0 && char.IsWhiteSpace(line[i])) i--;
        if (i < 0 || line[i] is not ('(' or ',')) return null;

        if (CallAt(line, i) is not { } call) return null;
        var before = line[..call.NameStart].TrimEnd();
        if (before.EndsWith("function", StringComparison.Ordinal) &&
            (before.Length == 8 || !IsNameChar(before[^9])))
            return null;
        return new ArgumentContext(call.Name, call.ParamIndex, false);
    }

    // Detects whether the cursor is inside a string literal that is an argument
    // to a function call (e.g., require("...") or Find_Player("...")).
    private static ArgumentContext? TryStringArgContext(string line, int col)
    {
        // Walk backward to find the opening quote of the string we may be inside
        var i = col - 1;
        while (i >= 0)
        {
            var ch = line[i];
            if (ch is '"' or '\'') break;
            if (ch is ')' or ';') return null;
            i--;
        }

        if (i < 0) return null;
        return CallAt(line, i - 1) is { } call ? new ArgumentContext(call.Name, call.ParamIndex) : null;
    }

    // The call whose argument list holds position start: walks back counting commas at depth
    // zero to the open paren, then reads the function name before it.
    private static (string Name, int NameStart, int ParamIndex)? CallAt(string line, int start)
    {
        var i = start;
        var paramIndex = 0;
        var depth = 0;

        while (i >= 0)
        {
            var ch = line[i];
            if (ch is ')' or ']' or '}')
            {
                depth++;
                i--;
                continue;
            }

            if (ch is '(' or '[' or '{')
            {
                if (depth > 0)
                {
                    depth--;
                    i--;
                    continue;
                }

                break;
            }

            if (ch == ',' && depth == 0) paramIndex++;
            if (ch is ';' or '\n') return null;
            i--;
        }

        if (i < 0 || line[i] != '(') return null;
        var parenPos = i;

        // Extract function name immediately before the open paren
        i = parenPos - 1;
        while (i >= 0 && char.IsWhiteSpace(line[i])) i--;
        var nameEnd = i;
        if (nameEnd < 0) return null;

        while (i >= 0 && IsNameChar(line[i])) i--;
        var nameStart = i + 1;

        if (nameStart > nameEnd) return null;
        return (line[nameStart..(nameEnd + 1)], nameStart, paramIndex);
    }

    private static bool IsNameChar(char ch)
    {
        return char.IsLetterOrDigit(ch) || ch == '_';
    }
}